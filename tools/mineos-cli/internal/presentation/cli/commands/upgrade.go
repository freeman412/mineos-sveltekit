package commands

import (
	"archive/tar"
	"archive/zip"
	"compress/gzip"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"runtime"
	"strings"

	"github.com/spf13/cobra"
)

const (
	githubRepo    = "freemancraft/mineos-sveltekit"
	githubAPIBase = "https://api.github.com"
	releasesURL   = githubAPIBase + "/repos/" + githubRepo + "/releases/latest"
)

type githubRelease struct {
	TagName string        `json:"tag_name"`
	Assets  []githubAsset `json:"assets"`
}

type githubAsset struct {
	Name               string `json:"name"`
	BrowserDownloadURL string `json:"browser_download_url"`
}

func NewUpgradeCommand(currentVersion string) *cobra.Command {
	var force bool
	var check bool

	cmd := &cobra.Command{
		Use:   "upgrade",
		Short: "Upgrade the mineos CLI to the latest version",
		Long: `Upgrade the mineos CLI binary to the latest version from GitHub releases.

This command downloads and replaces the current CLI binary. It does NOT
update the MineOS Docker containers - use 'mineos stack update' for that.

Examples:
  mineos upgrade          # Upgrade to latest version
  mineos upgrade --check  # Check for updates without installing
  mineos upgrade --force  # Force upgrade even if already on latest`,
		RunE: func(cmd *cobra.Command, _ []string) error {
			return runUpgrade(cmd, currentVersion, force, check)
		},
	}

	cmd.Flags().BoolVar(&force, "force", false, "Force upgrade even if already on latest version")
	cmd.Flags().BoolVar(&check, "check", false, "Check for updates without installing")

	return cmd
}

func runUpgrade(cmd *cobra.Command, currentVersion string, force, checkOnly bool) error {
	out := cmd.OutOrStdout()

	fmt.Fprintln(out, "Checking for updates...")

	release, err := fetchLatestRelease()
	if err != nil {
		return fmt.Errorf("failed to check for updates: %w", err)
	}

	latestVersion := release.TagName
	fmt.Fprintf(out, "Current version: %s\n", currentVersion)
	fmt.Fprintf(out, "Latest version:  %s\n", latestVersion)

	// Normalize versions for comparison (strip 'v' prefix if present)
	currentNorm := strings.TrimPrefix(currentVersion, "v")
	latestNorm := strings.TrimPrefix(latestVersion, "v")

	if currentNorm == latestNorm && !force {
		fmt.Fprintln(out, "You are already running the latest version.")
		return nil
	}

	if currentNorm == "dev" {
		fmt.Fprintln(out, "Warning: Running development version.")
		if !force {
			fmt.Fprintln(out, "Use --force to upgrade from dev version.")
			return nil
		}
	}

	if checkOnly {
		if currentNorm != latestNorm {
			fmt.Fprintln(out, "")
			fmt.Fprintln(out, "A new version is available!")
			fmt.Fprintln(out, "Run 'mineos upgrade' to install it.")
		}
		return nil
	}

	// Find the right asset for this OS/arch
	assetName := getAssetName()
	var downloadURL string
	for _, asset := range release.Assets {
		if asset.Name == assetName {
			downloadURL = asset.BrowserDownloadURL
			break
		}
	}

	if downloadURL == "" {
		return fmt.Errorf("no release found for %s/%s (looking for %s)", runtime.GOOS, runtime.GOARCH, assetName)
	}

	fmt.Fprintf(out, "\nDownloading %s...\n", assetName)

	// Get current executable path
	exePath, err := os.Executable()
	if err != nil {
		return fmt.Errorf("failed to get executable path: %w", err)
	}
	exePath, err = filepath.EvalSymlinks(exePath)
	if err != nil {
		return fmt.Errorf("failed to resolve executable path: %w", err)
	}

	// Download to temp file
	tmpFile, err := os.CreateTemp("", "mineos-upgrade-*")
	if err != nil {
		return fmt.Errorf("failed to create temp file: %w", err)
	}
	tmpPath := tmpFile.Name()
	defer os.Remove(tmpPath)

	resp, err := http.Get(downloadURL)
	if err != nil {
		tmpFile.Close()
		return fmt.Errorf("failed to download: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		tmpFile.Close()
		return fmt.Errorf("download failed with status: %s", resp.Status)
	}

	_, err = io.Copy(tmpFile, resp.Body)
	tmpFile.Close()
	if err != nil {
		return fmt.Errorf("failed to save download: %w", err)
	}

	// Extract binary from archive
	fmt.Fprintln(out, "Extracting...")
	binaryPath, err := extractBinary(tmpPath, assetName)
	if err != nil {
		return fmt.Errorf("failed to extract: %w", err)
	}
	defer os.Remove(binaryPath)

	// Replace current executable
	fmt.Fprintln(out, "Installing...")
	if err := replaceBinary(exePath, binaryPath); err != nil {
		return fmt.Errorf("failed to install: %w", err)
	}

	fmt.Fprintf(out, "\nSuccessfully upgraded to %s!\n", latestVersion)
	return nil
}

func fetchLatestRelease() (*githubRelease, error) {
	resp, err := http.Get(releasesURL)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusNotFound {
		return nil, errors.New("no releases found")
	}
	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("GitHub API returned status: %s", resp.Status)
	}

	var release githubRelease
	if err := json.NewDecoder(resp.Body).Decode(&release); err != nil {
		return nil, fmt.Errorf("failed to parse release info: %w", err)
	}

	return &release, nil
}

func getAssetName() string {
	os := runtime.GOOS
	arch := runtime.GOARCH

	// Map to release asset naming convention
	switch arch {
	case "amd64":
		arch = "x86_64"
	case "386":
		arch = "i386"
	}

	ext := ".tar.gz"
	if os == "windows" {
		ext = ".zip"
		return fmt.Sprintf("mineos_%s_%s%s", capitalizeOS(os), arch, ext)
	}

	return fmt.Sprintf("mineos_%s_%s%s", capitalizeOS(os), arch, ext)
}

func capitalizeOS(os string) string {
	switch os {
	case "darwin":
		return "Darwin"
	case "linux":
		return "Linux"
	case "windows":
		return "Windows"
	default:
		return os
	}
}

func extractBinary(archivePath, assetName string) (string, error) {
	if strings.HasSuffix(assetName, ".zip") {
		return extractZip(archivePath)
	}
	return extractTarGz(archivePath)
}

func extractTarGz(archivePath string) (string, error) {
	file, err := os.Open(archivePath)
	if err != nil {
		return "", err
	}
	defer file.Close()

	gzr, err := gzip.NewReader(file)
	if err != nil {
		return "", err
	}
	defer gzr.Close()

	tr := tar.NewReader(gzr)
	for {
		header, err := tr.Next()
		if err == io.EOF {
			break
		}
		if err != nil {
			return "", err
		}

		// Look for the mineos binary
		if header.Typeflag == tar.TypeReg && (header.Name == "mineos" || filepath.Base(header.Name) == "mineos") {
			tmpFile, err := os.CreateTemp("", "mineos-binary-*")
			if err != nil {
				return "", err
			}
			defer tmpFile.Close()

			if _, err := io.Copy(tmpFile, tr); err != nil {
				os.Remove(tmpFile.Name())
				return "", err
			}

			return tmpFile.Name(), nil
		}
	}

	return "", errors.New("mineos binary not found in archive")
}

func extractZip(archivePath string) (string, error) {
	r, err := zip.OpenReader(archivePath)
	if err != nil {
		return "", err
	}
	defer r.Close()

	for _, f := range r.File {
		name := filepath.Base(f.Name)
		if name == "mineos.exe" || name == "mineos" {
			rc, err := f.Open()
			if err != nil {
				return "", err
			}
			defer rc.Close()

			tmpFile, err := os.CreateTemp("", "mineos-binary-*")
			if err != nil {
				return "", err
			}
			defer tmpFile.Close()

			if _, err := io.Copy(tmpFile, rc); err != nil {
				os.Remove(tmpFile.Name())
				return "", err
			}

			return tmpFile.Name(), nil
		}
	}

	return "", errors.New("mineos binary not found in archive")
}

func replaceBinary(oldPath, newPath string) error {
	// Get permissions from old binary
	info, err := os.Stat(oldPath)
	if err != nil {
		return err
	}

	// On Windows, we can't replace a running executable directly
	// Move old one aside, copy new one, then delete old
	if runtime.GOOS == "windows" {
		backupPath := oldPath + ".old"
		os.Remove(backupPath) // Remove any existing backup

		if err := os.Rename(oldPath, backupPath); err != nil {
			return fmt.Errorf("failed to backup old binary: %w", err)
		}

		if err := copyFileWithMode(newPath, oldPath, info.Mode()); err != nil {
			// Try to restore backup
			os.Rename(backupPath, oldPath)
			return fmt.Errorf("failed to install new binary: %w", err)
		}

		// Clean up backup (may fail if still in use, that's ok)
		os.Remove(backupPath)
		return nil
	}

	// On Unix, we can atomically replace
	if err := copyFileWithMode(newPath, oldPath+".new", info.Mode()); err != nil {
		return err
	}

	if err := os.Rename(oldPath+".new", oldPath); err != nil {
		os.Remove(oldPath + ".new")
		return err
	}

	return nil
}

func copyFileWithMode(src, dst string, mode os.FileMode) error {
	srcFile, err := os.Open(src)
	if err != nil {
		return err
	}
	defer srcFile.Close()

	dstFile, err := os.OpenFile(dst, os.O_WRONLY|os.O_CREATE|os.O_TRUNC, mode)
	if err != nil {
		return err
	}
	defer dstFile.Close()

	_, err = io.Copy(dstFile, srcFile)
	return err
}

// CheckForUpdates checks if a newer version is available and returns a message if so.
// Returns empty string if no update available or on error.
func CheckForUpdates(currentVersion string) string {
	// Don't check for dev versions
	if currentVersion == "dev" || currentVersion == "" {
		return ""
	}

	release, err := fetchLatestRelease()
	if err != nil {
		return ""
	}

	currentNorm := strings.TrimPrefix(currentVersion, "v")
	latestNorm := strings.TrimPrefix(release.TagName, "v")

	if currentNorm != latestNorm {
		return fmt.Sprintf("A new version of mineos CLI is available: %s (current: %s). Run 'mineos upgrade' to update.", release.TagName, currentVersion)
	}

	return ""
}
