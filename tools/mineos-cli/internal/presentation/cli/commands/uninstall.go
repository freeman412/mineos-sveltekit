package commands

import (
	"archive/zip"
	"bufio"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
	"time"

	"github.com/spf13/cobra"
)

const uninstallBanner = `  __  __ _             ___  ____
 |  \/  (_)_ __   ___ / _ \/ ___|
 | |\/| | | '_ \ / _ \ | | \___ \
 | |  | | | | | |  __/ |_| |___) |
 |_|  |_|_|_| |_|\___|\___/|____/ `

type uninstallOptions struct {
	mode        string
	skipConfirm bool
	removeVols  bool
	removeCLI   bool
	removeAll   bool
}

var errUninstallCancelled = errors.New("uninstall cancelled")

func NewUninstallCommand() *cobra.Command {
	opts := uninstallOptions{}

	cmd := &cobra.Command{
		Use:   "uninstall",
		Short: "Remove MineOS containers and optionally local data",
		RunE: func(cmd *cobra.Command, _ []string) error {
			return runUninstall(cmd, opts)
		},
	}

	cmd.Flags().StringVar(&opts.mode, "mode", "", "Uninstall mode: containers|backup|remove|complete (interactive if empty)")
	cmd.Flags().BoolVar(&opts.skipConfirm, "yes", false, "Skip the DELETE confirmation for destructive options")
	cmd.Flags().BoolVar(&opts.removeVols, "volumes", false, "Also remove Docker volumes when deleting data")
	cmd.Flags().BoolVar(&opts.removeCLI, "remove-cli", false, "Remove CLI from system PATH")
	cmd.Flags().BoolVar(&opts.removeAll, "remove-all", false, "Remove everything including the MineOS installation directory")

	return cmd
}

func runUninstall(cmd *cobra.Command, opts uninstallOptions) error {
	out := cmd.OutOrStdout()

	if _, err := exec.LookPath("docker"); err != nil {
		return errors.New("docker is not installed")
	}

	compose, err := detectCompose()
	if err != nil {
		return err
	}

	mode, err := resolveUninstallMode(cmd, opts.mode)
	if err != nil {
		return err
	}

	fmt.Fprintln(out, uninstallBanner)
	fmt.Fprintln(out, "MineOS Uninstall")
	fmt.Fprintln(out, "")

	switch mode {
	case "containers":
		fmt.Fprintln(out, "Removing containers (keeping all data)...")
		if err := compose.down(false); err != nil {
			return err
		}
		fmt.Fprintln(out, "✓ Containers removed. Data preserved.")

	case "backup":
		fmt.Fprintln(out, "Backing up data and removing everything...")
		if err := confirmDestructive(cmd, opts.skipConfirm); err != nil {
			if errors.Is(err, errUninstallCancelled) {
				return nil
			}
			return err
		}
		if err := compose.down(false); err != nil {
			return err
		}
		backupRoot, err := backupData(out)
		if err != nil {
			return err
		}
		if err := compose.down(shouldRemoveVolumes(opts)); err != nil {
			return err
		}
		if err := removeLocalData(out); err != nil {
			return err
		}
		fmt.Fprintf(out, "✓ Containers and data removed. Backup created at %s\n", backupRoot)

	case "remove":
		fmt.Fprintln(out, "Removing containers and data (no backup)...")
		if err := confirmDestructive(cmd, opts.skipConfirm); err != nil {
			if errors.Is(err, errUninstallCancelled) {
				return nil
			}
			return err
		}
		if err := compose.down(shouldRemoveVolumes(opts)); err != nil {
			return err
		}
		if err := removeLocalData(out); err != nil {
			return err
		}
		fmt.Fprintln(out, "✓ Containers and data removed.")

	case "complete":
		fmt.Fprintln(out, "Complete uninstall - removing EVERYTHING...")
		if err := confirmDestructive(cmd, opts.skipConfirm); err != nil {
			if errors.Is(err, errUninstallCancelled) {
				return nil
			}
			return err
		}

		// Stop containers and remove data
		if err := compose.down(shouldRemoveVolumes(opts)); err != nil {
			fmt.Fprintf(out, "Warning: Failed to stop containers: %v\n", err)
		}
		if err := removeLocalData(out); err != nil {
			fmt.Fprintf(out, "Warning: Failed to remove data: %v\n", err)
		}

		// Remove CLI from PATH if requested or asked
		removeCLI := opts.removeCLI
		if !removeCLI && !cmd.Flags().Changed("remove-cli") {
			reader := bufio.NewReader(os.Stdin)
			remove, err := promptYesNo(reader, out, "Remove 'mineos' command from system PATH", true)
			if err == nil {
				removeCLI = remove
			}
		}

		if removeCLI {
			if err := removeCLIFromPath(out); err != nil {
				fmt.Fprintf(out, "Warning: Failed to remove CLI from PATH: %v\n", err)
			}
		}

		// Remove entire installation directory
		removeDir := opts.removeAll
		if !removeDir && !cmd.Flags().Changed("remove-all") {
			fmt.Fprintln(out, "")
			fmt.Fprintln(out, "⚠️  FINAL STEP: Remove the entire MineOS installation directory?")
			fmt.Fprintln(out, "This will delete all files including docker-compose.yml, .env, and this CLI.")
			reader := bufio.NewReader(os.Stdin)
			remove, err := promptYesNo(reader, out, "Remove installation directory", true)
			if err == nil {
				removeDir = remove
			}
		}

		if removeDir {
			if err := removeInstallationDirectory(out); err != nil {
				return fmt.Errorf("failed to remove installation directory: %w", err)
			}
		}

		fmt.Fprintln(out, "")
		fmt.Fprintln(out, "✓ Complete uninstall finished!")
		fmt.Fprintln(out, "")
		if removeDir {
			fmt.Fprintln(out, "MineOS has been completely removed from your system.")
			fmt.Fprintln(out, "This terminal will close automatically.")
		}
		return nil

	default:
		return fmt.Errorf("unknown uninstall mode: %s", mode)
	}

	fmt.Fprintln(out, "")
	fmt.Fprintln(out, "Additional cleanup:")
	fmt.Fprintln(out, "  - To remove Docker images: docker image prune -a")
	fmt.Fprintln(out, "  - For complete uninstall: mineos uninstall --mode complete")
	return nil
}

func resolveUninstallMode(cmd *cobra.Command, mode string) (string, error) {
	mode = strings.TrimSpace(strings.ToLower(mode))
	if mode == "" {
		return promptUninstallMode(cmd)
	}

	switch mode {
	case "1", "containers", "container", "keep":
		return "containers", nil
	case "2", "backup":
		return "backup", nil
	case "3", "remove", "delete":
		return "remove", nil
	case "4", "complete", "full", "everything":
		return "complete", nil
	default:
		return "", fmt.Errorf("invalid mode: %s", mode)
	}
}

func promptUninstallMode(cmd *cobra.Command) (string, error) {
	fmt.Println("")
	fmt.Println("Choose an uninstall option:")
	fmt.Println("  1) Remove containers only (keep all data) [default]")
	fmt.Println("  2) Backup data then remove containers and data")
	fmt.Println("  3) Remove containers and data without backup")
	fmt.Println("  4) Complete uninstall (remove EVERYTHING including CLI and installation directory)")
	fmt.Print("Enter choice [1-4]: ")

	scanner := bufio.NewScanner(os.Stdin)
	if !scanner.Scan() {
		if err := scanner.Err(); err != nil {
			return "", err
		}
		return resolveUninstallMode(cmd, "1")
	}
	choice := strings.TrimSpace(scanner.Text())
	if choice == "" {
		choice = "1"
	}
	return resolveUninstallMode(cmd, choice)
}

func confirmDestructive(cmd *cobra.Command, skip bool) error {
	if skip {
		return nil
	}
	fmt.Println("This will permanently delete database files and local data.")
	fmt.Print("Type DELETE to continue: ")

	scanner := bufio.NewScanner(os.Stdin)
	if !scanner.Scan() {
		if err := scanner.Err(); err != nil {
			return err
		}
		fmt.Println("Uninstall cancelled.")
		return errUninstallCancelled
	}
	if strings.TrimSpace(scanner.Text()) != "DELETE" {
		fmt.Println("Uninstall cancelled.")
		return errUninstallCancelled
	}
	return nil
}

func backupData(out io.Writer) (string, error) {
	timestamp := time.Now().Format("20060102-150405")
	backupRoot := filepath.Join("backups", "mineos-uninstall-"+timestamp)

	if err := os.MkdirAll(backupRoot, 0o755); err != nil {
		return "", err
	}

	if _, err := os.Stat("data"); err == nil {
		fmt.Fprintln(out, "Backing up local data folder...")
		zipPath := filepath.Join(backupRoot, "sqlite-data.zip")
		if err := zipDir("data", zipPath); err != nil {
			return "", err
		}
	}

	if _, err := os.Stat(".env"); err == nil {
		if err := copyFile(".env", filepath.Join(backupRoot, "env.backup")); err != nil {
			return "", err
		}
	}

	return backupRoot, nil
}

func removeLocalData(out io.Writer) error {
	if _, err := os.Stat("data"); err == nil {
		fmt.Fprintln(out, "Removing local data folder...")
		return os.RemoveAll("data")
	}
	return nil
}

func zipDir(srcDir, destZip string) error {
	zipFile, err := os.Create(destZip)
	if err != nil {
		return err
	}
	defer zipFile.Close()

	writer := zip.NewWriter(zipFile)
	defer writer.Close()

	return filepath.WalkDir(srcDir, func(path string, d os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if d.IsDir() {
			return nil
		}
		relPath, err := filepath.Rel(srcDir, path)
		if err != nil {
			return err
		}

		zipPath := filepath.ToSlash(relPath)
		file, err := os.Open(path)
		if err != nil {
			return err
		}
		defer file.Close()

		info, err := d.Info()
		if err != nil {
			return err
		}
		header, err := zip.FileInfoHeader(info)
		if err != nil {
			return err
		}
		header.Name = zipPath
		header.Method = zip.Deflate

		writerEntry, err := writer.CreateHeader(header)
		if err != nil {
			return err
		}
		_, err = io.Copy(writerEntry, file)
		return err
	})
}

func copyFile(src, dest string) error {
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()

	if err := os.MkdirAll(filepath.Dir(dest), 0o755); err != nil {
		return err
	}

	out, err := os.Create(dest)
	if err != nil {
		return err
	}
	defer out.Close()

	if _, err := io.Copy(out, in); err != nil {
		return err
	}
	return out.Sync()
}

type composeRunner struct {
	exe      string
	baseArgs []string
}

func detectCompose() (composeRunner, error) {
	if _, err := exec.LookPath("docker"); err == nil {
		cmd := exec.Command("docker", "compose", "version")
		if err := cmd.Run(); err == nil {
			return composeRunner{exe: "docker", baseArgs: []string{"compose"}}, nil
		}
	}

	if _, err := exec.LookPath("docker-compose"); err == nil {
		return composeRunner{exe: "docker-compose"}, nil
	}

	return composeRunner{}, errors.New("docker compose is not available")
}

func (c composeRunner) down(withVolumes bool) error {
	args := append([]string{}, c.baseArgs...)
	args = append(args, "down", "--remove-orphans")
	if withVolumes {
		args = append(args, "--volumes")
	}
	cmd := exec.Command(c.exe, args...)
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr
	return cmd.Run()
}

func shouldRemoveVolumes(opts uninstallOptions) bool {
	if opts.removeVols {
		return true
	}
	return runtime.GOOS == "windows"
}

func removeCLIFromPath(out io.Writer) error {
	if runtime.GOOS == "windows" {
		return removeCLIFromPathWindows(out)
	}
	return removeCLIFromPathUnix(out)
}

func removeCLIFromPathWindows(out io.Writer) error {
	localAppData := os.Getenv("LOCALAPPDATA")
	if localAppData == "" {
		return errors.New("LOCALAPPDATA environment variable not set")
	}

	installDir := filepath.Join(localAppData, "Programs", "MineOS")
	exePath := filepath.Join(installDir, "mineos.exe")

	if _, err := os.Stat(exePath); err == nil {
		if err := os.Remove(exePath); err != nil {
			return fmt.Errorf("failed to remove CLI: %w", err)
		}
		fmt.Fprintf(out, "✓ Removed CLI from: %s\n", exePath)

		// Try to remove the directory if empty
		os.Remove(installDir)
		os.Remove(filepath.Dir(installDir)) // Try to remove Programs dir if empty

		fmt.Fprintln(out, "")
		fmt.Fprintln(out, "Note: You may need to manually remove the PATH entry:")
		fmt.Fprintf(out, "  %s\n", installDir)
		fmt.Fprintln(out, "Follow the same steps as during installation to edit your PATH.")
	} else {
		fmt.Fprintln(out, "CLI not found in system PATH location.")
	}

	return nil
}

func removeCLIFromPathUnix(out io.Writer) error {
	// Check both possible install locations
	systemBin := "/usr/local/bin/mineos"
	homeDir, _ := os.UserHomeDir()
	userBin := ""
	if homeDir != "" {
		userBin = filepath.Join(homeDir, ".local", "bin", "mineos")
	}

	removed := false

	if _, err := os.Stat(systemBin); err == nil {
		if err := os.Remove(systemBin); err != nil {
			return fmt.Errorf("failed to remove CLI from %s: %w", systemBin, err)
		}
		fmt.Fprintf(out, "✓ Removed CLI from: %s\n", systemBin)
		removed = true
	}

	if userBin != "" {
		if _, err := os.Stat(userBin); err == nil {
			if err := os.Remove(userBin); err != nil {
				return fmt.Errorf("failed to remove CLI from %s: %w", userBin, err)
			}
			fmt.Fprintf(out, "✓ Removed CLI from: %s\n", userBin)
			removed = true
		}
	}

	if !removed {
		fmt.Fprintln(out, "CLI not found in system PATH locations.")
	}

	return nil
}

func removeInstallationDirectory(out io.Writer) error {
	// Get the current working directory (should be the installation directory)
	cwd, err := os.Getwd()
	if err != nil {
		return fmt.Errorf("failed to get current directory: %w", err)
	}

	fmt.Fprintf(out, "Removing installation directory: %s\n", cwd)

	// On Windows, we need to schedule deletion after exit
	if runtime.GOOS == "windows" {
		// Create a batch file to delete everything after the process exits
		batchFile := filepath.Join(os.TempDir(), "mineos-uninstall.bat")
		script := fmt.Sprintf("@echo off\ntimeout /t 2 /nobreak >nul\nrd /s /q \"%s\"\ndel \"%%~f0\"\n", cwd)
		if err := os.WriteFile(batchFile, []byte(script), 0o755); err != nil {
			return fmt.Errorf("failed to create cleanup script: %w", err)
		}

		// Execute the batch file
		cmd := exec.Command("cmd", "/C", "start", "/min", batchFile)
		if err := cmd.Start(); err != nil {
			return fmt.Errorf("failed to start cleanup script: %w", err)
		}

		fmt.Fprintln(out, "✓ Cleanup scheduled. Directory will be removed in 2 seconds.")
		return nil
	}

	// On Unix, we can delete everything except the running executable
	// Then use a shell script to delete the executable after exit
	parentDir := filepath.Dir(cwd)

	// Create a cleanup script
	scriptPath := filepath.Join(os.TempDir(), "mineos-cleanup.sh")
	script := fmt.Sprintf("#!/bin/sh\nsleep 2\nrm -rf \"%s\"\nrm -f \"$0\"\n", cwd)
	if err := os.WriteFile(scriptPath, []byte(script), 0o755); err != nil {
		return fmt.Errorf("failed to create cleanup script: %w", err)
	}

	// Execute the cleanup script in the background
	cmd := exec.Command("sh", scriptPath)
	cmd.Dir = parentDir
	if err := cmd.Start(); err != nil {
		return fmt.Errorf("failed to start cleanup script: %w", err)
	}

	fmt.Fprintln(out, "✓ Cleanup scheduled. Directory will be removed in 2 seconds.")
	return nil
}
