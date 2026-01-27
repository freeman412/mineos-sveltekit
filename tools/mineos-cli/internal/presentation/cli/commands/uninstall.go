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

type uninstallOptions struct {
	mode        string
	skipConfirm bool
	removeVols  bool
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

	cmd.Flags().StringVar(&opts.mode, "mode", "", "Uninstall mode: containers|backup|remove (interactive if empty)")
	cmd.Flags().BoolVar(&opts.skipConfirm, "yes", false, "Skip the DELETE confirmation for destructive options")
	cmd.Flags().BoolVar(&opts.removeVols, "volumes", false, "Also remove Docker volumes when deleting data")

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

	fmt.Fprintln(out, "MineOS Uninstall")
	fmt.Fprintln(out, "This will stop and remove MineOS containers.")

	switch mode {
	case "containers":
		if err := compose.down(false); err != nil {
			return err
		}
		fmt.Fprintln(out, "Containers removed. Data preserved.")
	case "backup":
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
		fmt.Fprintf(out, "Containers and data removed. Backup created at %s.\n", backupRoot)
	case "remove":
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
		fmt.Fprintln(out, "Containers and data removed.")
	default:
		return fmt.Errorf("unknown uninstall mode: %s", mode)
	}

	fmt.Fprintln(out, "If you want to remove Docker images too, run: docker image prune -a")
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
	fmt.Print("Enter choice [1-3]: ")

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
