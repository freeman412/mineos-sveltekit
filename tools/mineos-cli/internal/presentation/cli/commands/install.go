package commands

import (
	"bufio"
	"context"
	"crypto/rand"
	"encoding/base64"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"time"

	"github.com/charmbracelet/lipgloss"
	"github.com/spf13/cobra"
	"golang.org/x/term"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/telemetry"
)

// Installer color palette — consistent with TUI styles
var (
	// Core colors
	styleBold    = lipgloss.NewStyle().Bold(true)
	styleTitle   = lipgloss.NewStyle().Foreground(lipgloss.Color("39")).Bold(true)
	styleSuccess = lipgloss.NewStyle().Foreground(lipgloss.Color("70")).Bold(true)
	styleWarning = lipgloss.NewStyle().Foreground(lipgloss.Color("214")).Bold(true)
	styleError   = lipgloss.NewStyle().Foreground(lipgloss.Color("196")).Bold(true)
	styleDim     = lipgloss.NewStyle().Foreground(lipgloss.Color("246"))
	styleAccent  = lipgloss.NewStyle().Foreground(lipgloss.Color("205"))
	styleInfo    = lipgloss.NewStyle().Foreground(lipgloss.Color("81"))
	styleValue   = lipgloss.NewStyle().Foreground(lipgloss.Color("75")).Bold(true)
	styleLabel   = lipgloss.NewStyle().Foreground(lipgloss.Color("255"))
	styleStep    = lipgloss.NewStyle().Foreground(lipgloss.Color("135")).Bold(true)

	// Decorative
	styleBanner = lipgloss.NewStyle().Foreground(lipgloss.Color("39")).Bold(true)

	// Box for the completion message
	styleBox = lipgloss.NewStyle().
			Border(lipgloss.RoundedBorder()).
			BorderForeground(lipgloss.Color("70")).
			Padding(0, 2)
)

type installOptions struct {
	adminUser        string
	adminPass        string
	apiKey           string
	hostBaseDir      string
	dataDir          string
	apiPort          int
	webPort          int
	webOrigin        string
	minecraftHost    string
	bodySizeLimit    string
	networkMode      string
	buildFromSource  bool
	imageTag         string
	quiet            bool
	skipPathInstall  bool
	telemetryEnabled bool
}

const (
	defaultHostBaseDir   = "./minecraft"
	defaultDataDir       = "./data"
	defaultNetworkMode   = "bridge"
	defaultApiPort       = 5078
	defaultWebPort       = 3000
	defaultBodySizeLimit = "Infinity"
	containerBaseDir     = "/var/games/minecraft"

	installBanner = `  __  __ _             ___  ____
 |  \/  (_)_ __   ___ / _ \/ ___|
 | |\/| | | '_ \ / _ \ | | \___ \
 | |  | | | | | |  __/ |_| |___) |
 |_|  |_|_|_| |_|\___|\___/|____/ `

	installBannerTagline = "Minecraft Server Management"
)

func NewInstallCommand() *cobra.Command {
	opts := installOptions{}

	cmd := &cobra.Command{
		Use:   "install",
		Short: "Interactive installer for MineOS",
		RunE: func(cmd *cobra.Command, _ []string) error {
			return runInstall(cmd, opts)
		},
	}

	cmd.Flags().StringVar(&opts.adminUser, "admin", "", "Admin username")
	cmd.Flags().StringVar(&opts.adminPass, "password", "", "Admin password")
	cmd.Flags().StringVar(&opts.apiKey, "api-key", "", "Management API key (optional)")
	cmd.Flags().StringVar(&opts.hostBaseDir, "host-dir", "", "Host storage directory (relative)")
	cmd.Flags().StringVar(&opts.dataDir, "data-dir", "", "Data directory (relative)")
	cmd.Flags().IntVar(&opts.apiPort, "api-port", 0, "API port")
	cmd.Flags().IntVar(&opts.webPort, "web-port", 0, "Web UI port")
	cmd.Flags().StringVar(&opts.webOrigin, "web-origin", "", "Web UI origin")
	cmd.Flags().StringVar(&opts.minecraftHost, "minecraft-host", "", "Public Minecraft host")
	cmd.Flags().StringVar(&opts.bodySizeLimit, "body-size-limit", "", "Web UI upload body size limit")
	cmd.Flags().StringVar(&opts.networkMode, "network-mode", "", "Docker network mode (bridge|host)")
	cmd.Flags().BoolVar(&opts.buildFromSource, "build", false, "Build images from source instead of pulling")
	cmd.Flags().StringVar(&opts.imageTag, "image-tag", "", "Image tag to pull when not building from source")
	cmd.Flags().BoolVarP(&opts.quiet, "quiet", "q", false, "Non-interactive mode (requires --admin, --password)")
	cmd.Flags().BoolVar(&opts.skipPathInstall, "skip-path-install", false, "Skip prompting to install CLI to PATH")

	return cmd
}

func runInstall(cmd *cobra.Command, opts installOptions) error {
	out := cmd.OutOrStdout()
	var reader *bufio.Reader // kept for function signature compatibility

	if err := ensureDockerAvailable(); err != nil {
		return err
	}

	// Validate Docker is actually running (not just installed)
	if err := ensureDockerRunning(); err != nil {
		return err
	}

	compose, err := detectCompose()
	if err != nil {
		return err
	}

	// In quiet mode, validate required fields
	if opts.quiet {
		if opts.adminUser == "" {
			return errors.New("--admin is required in quiet mode")
		}
		if opts.adminPass == "" {
			return errors.New("--password is required in quiet mode")
		}
		// Apply defaults for optional fields
		if opts.hostBaseDir == "" {
			opts.hostBaseDir = defaultHostBaseDir
		}
		if opts.dataDir == "" {
			opts.dataDir = defaultDataDir
		}
		if opts.apiPort == 0 {
			opts.apiPort = defaultApiPort
		}
		if opts.webPort == 0 {
			opts.webPort = defaultWebPort
		}
		if opts.webOrigin == "" {
			opts.webOrigin = fmt.Sprintf("http://localhost:%d", opts.webPort)
		}
		if opts.minecraftHost == "" {
			opts.minecraftHost = "localhost"
		}
		if opts.bodySizeLimit == "" {
			opts.bodySizeLimit = defaultBodySizeLimit
		}
		if opts.networkMode == "" {
			opts.networkMode = defaultNetworkMode
		}
		if !opts.buildFromSource && opts.imageTag == "" {
			opts.imageTag = "latest"
		}
	}

	if _, err := os.Stat(".env"); err == nil {
		if opts.quiet {
			// In quiet mode, overwrite without prompting
			fmt.Fprintln(out, styleWarning.Render("Overwriting existing .env file..."))
		} else {
			overwrite, err := promptYesNo(reader, out, ".env already exists. Overwrite", false)
			if err != nil {
				return err
			}
			if !overwrite {
				return errors.New("install cancelled")
			}
		}
	}

	if !opts.quiet {
		fmt.Fprintln(out, styleBanner.Render(installBanner))
		fmt.Fprintln(out, styleAccent.Render(installBannerTagline))
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleTitle.Render("Welcome to the MineOS installer!"))
		fmt.Fprintln(out, styleDim.Render("This will set up everything you need to manage Minecraft servers."))
		fmt.Fprintln(out, styleDim.Render("Press Enter to accept the default values shown in parentheses."))
		fmt.Fprintln(out, "")
	}

	if opts.adminUser == "" && !opts.quiet {
		value, err := promptString(reader, out, "Admin username", "admin")
		if err != nil {
			return err
		}
		opts.adminUser = value
	}

	if opts.adminPass == "" && !opts.quiet {
		for {
			value, err := promptPassword(out, "Admin password: ")
			if err != nil {
				return err
			}
			if strings.TrimSpace(value) == "" {
				fmt.Fprintln(out, "Password cannot be empty.")
				continue
			}
			opts.adminPass = value
			break
		}
	}

	if opts.hostBaseDir == "" && !opts.quiet {
		value, err := promptRelativePath(reader, out, "Local storage directory for Minecraft servers (relative)", defaultHostBaseDir)
		if err != nil {
			return err
		}
		opts.hostBaseDir = value
	} else if opts.hostBaseDir != "" && !isValidRelativePath(opts.hostBaseDir) {
		return fmt.Errorf("host-dir must be relative (no leading /, ~, or ..)")
	}

	if opts.dataDir == "" && !opts.quiet {
		value, err := promptRelativePath(reader, out, "Database directory (relative)", defaultDataDir)
		if err != nil {
			return err
		}
		opts.dataDir = value
	} else if opts.dataDir != "" && !isValidRelativePath(opts.dataDir) {
		return fmt.Errorf("data-dir must be relative (no leading /, ~, or ..)")
	}

	if opts.apiPort == 0 && !opts.quiet {
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleStep.Render("Backend API port")+" "+styleDim.Render("- Used internally by the server (usually keep default)"))
		value, err := promptInt(reader, out, "API port", defaultApiPort)
		if err != nil {
			return err
		}
		opts.apiPort = value
	}

	if opts.webPort == 0 && !opts.quiet {
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleStep.Render("Web interface port")+" "+styleDim.Render("- This is the port you'll type in your browser"))
		fmt.Fprintln(out, styleDim.Render("Example: http://localhost:3000 - You can change this if 3000 is already in use"))
		value, err := promptInt(reader, out, "Web UI port", defaultWebPort)
		if err != nil {
			return err
		}
		opts.webPort = value
	}

	if opts.webOrigin == "" && !opts.quiet {
		defaultOrigin := fmt.Sprintf("http://localhost:%d", opts.webPort)
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleStep.Render("Web interface URL")+" "+styleDim.Render("- The full address you'll use in your browser"))
		fmt.Fprintln(out, styleDim.Render("If running on this computer, use 'localhost'. If accessing from other devices,"))
		fmt.Fprintln(out, styleDim.Render("replace 'localhost' with this computer's IP address (e.g., http://192.168.1.100:3000)"))
		value, err := promptString(reader, out, "Web UI origin", defaultOrigin)
		if err != nil {
			return err
		}
		opts.webOrigin = value
	}

	if opts.minecraftHost == "" && !opts.quiet {
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleStep.Render("Minecraft server address")+" "+styleDim.Render("- What players will connect to"))
		fmt.Fprintln(out, styleDim.Render("  Local play: use 'localhost'"))
		fmt.Fprintln(out, styleDim.Render("  LAN/friends: use this computer's local IP (e.g., 192.168.1.100)"))
		fmt.Fprintln(out, styleDim.Render("  Internet: use your public IP or domain name (e.g., mc.example.com)"))
		value, err := promptString(reader, out, "Public Minecraft host", "localhost")
		if err != nil {
			return err
		}
		opts.minecraftHost = value
	}

	if opts.bodySizeLimit == "" && !opts.quiet {
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleStep.Render("Upload file size limit")+" "+styleDim.Render("- Maximum upload size through the web interface"))
		fmt.Fprintln(out, styleDim.Render("  'Infinity' = no limit, or specify a size like '500MB' or '1GB'"))
		fmt.Fprintln(out, styleDim.Render("  (Modpacks and world backups can be large, so 'Infinity' is recommended)"))
		value, err := promptString(reader, out, "Web UI upload size limit", defaultBodySizeLimit)
		if err != nil {
			return err
		}
		opts.bodySizeLimit = value
	}

	networkModeChanged := cmd.Flags().Changed("network-mode")
	if !networkModeChanged && !opts.quiet {
		if runtime.GOOS != "linux" {
			opts.networkMode = defaultNetworkMode
		} else {
			fmt.Fprintln(out, "")
			fmt.Fprintln(out, styleStep.Render("Network Mode")+" "+styleDim.Render("- LAN discovery requires host networking on Linux"))
			value, err := promptYesNo(reader, out, "Enable host networking for LAN discovery", false)
			if err != nil {
				return err
			}
			if value {
				opts.networkMode = "host"
			} else {
				opts.networkMode = defaultNetworkMode
			}
		}
	} else if networkModeChanged {
		mode := strings.ToLower(strings.TrimSpace(opts.networkMode))
		if mode != "bridge" && mode != "host" {
			return fmt.Errorf("invalid network-mode: %s", opts.networkMode)
		}
		if mode == "host" && runtime.GOOS != "linux" {
			fmt.Fprintln(out, styleWarning.Render("Host networking is only supported on Linux.")+" Using bridge mode.")
			mode = defaultNetworkMode
		}
		opts.networkMode = mode
	}

	buildChanged := cmd.Flags().Changed("build")
	if !buildChanged && !opts.quiet {
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleStep.Render("Installation method:"))
		fmt.Fprintln(out, styleDim.Render("  - Pull images (recommended): Download pre-built software - faster and easier"))
		fmt.Fprintln(out, styleDim.Render("  - Build from source: Compile the software yourself - for developers only"))
		value, err := promptYesNo(reader, out, "Build from source instead of pulling pre-built images", false)
		if err != nil {
			return err
		}
		opts.buildFromSource = value
	}

	if !opts.buildFromSource {
		if opts.imageTag == "" && !opts.quiet {
			fmt.Fprintln(out, "")
			fmt.Fprintln(out, styleStep.Render("Version to install:"))
			fmt.Fprintln(out, styleDim.Render("  - 'latest': Most recent stable version (recommended)"))
			fmt.Fprintln(out, styleDim.Render("  - 'preview': Latest preview/pre-release version"))
			fmt.Fprintln(out, styleDim.Render("  - Or specify a version tag like 'v1.0.0' for a specific release"))
			value, err := promptString(reader, out, "Version tag", "latest")
			if err != nil {
				return err
			}
			opts.imageTag = value
		}
		if isPreviewTag(opts.imageTag) && !opts.quiet {
			fmt.Fprintln(out, "")
			fmt.Fprintln(out, styleWarning.Render("WARNING:")+" Preview versions may be unstable, contain bugs, or cause data loss.")
			fmt.Fprintln(out, styleWarning.Render("Do not use preview releases in production. Back up your data before upgrading."))
			fmt.Fprintln(out, "")
		}
	} else if !dirExists("apps") {
		return errors.New("source files not found (./apps missing); use the installer with --build after cloning the repo")
	}

	// Telemetry prompt (default opt-in)
	telemetryEnabled := true // default opt-in
	if !opts.quiet {
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleStep.Render("Anonymous Telemetry:"))
		fmt.Fprintln(out, styleDim.Render("  Help improve MineOS by sharing anonymous usage data (OS, version, server count,"))
		fmt.Fprintln(out, styleDim.Render("  approximate location based on IP, and lifecycle events like startup/shutdown/crashes)."))
		fmt.Fprintln(out, styleDim.Render("  No personal information, player activity, or server names are collected."))
		fmt.Fprintln(out, styleDim.Render("  You can opt-out anytime by editing .env (")+styleInfo.Render("MINEOS_TELEMETRY_ENABLED=false")+styleDim.Render(")"))
		value, err := promptYesNo(reader, out, "Enable anonymous telemetry", true)
		if err != nil {
			return err
		}
		telemetryEnabled = value
	}
	opts.telemetryEnabled = telemetryEnabled

	if !opts.quiet {
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleInfo.Render("Tip:")+" "+styleDim.Render("Integrations like CurseForge API keys can be configured later"))
		fmt.Fprintln(out, styleDim.Render("in the web UI under Settings > Integrations."))
	}

	// Generate installation ID for telemetry tracking
	installationID := telemetry.GenerateInstallationID()

	// Track installation start time for telemetry
	installStart := time.Now()

	jwtSecret, err := randomToken(32)
	if err != nil {
		return err
	}

	apiKey := opts.apiKey
	if apiKey == "" {
		apiKey, err = randomToken(32)
		if err != nil {
			return err
		}
	}

	caddySite := deriveCaddySite(opts.webOrigin)

	envContents := renderEnv(envConfig{
		adminUser:        opts.adminUser,
		adminPass:        opts.adminPass,
		jwtSecret:        jwtSecret,
		apiKey:           apiKey,
		hostBaseDir:      opts.hostBaseDir,
		dataDir:          opts.dataDir,
		networkMode:      opts.networkMode,
		buildFromSource:  opts.buildFromSource,
		imageTag:         opts.imageTag,
		apiPort:          opts.apiPort,
		webPort:          opts.webPort,
		webOrigin:        opts.webOrigin,
		caddySite:        caddySite,
		minecraftHost:    opts.minecraftHost,
		bodySizeLimit:    opts.bodySizeLimit,
		telemetryEnabled: opts.telemetryEnabled,
		installationID:   installationID,
	})

	if err := os.WriteFile(".env", []byte(envContents), 0o644); err != nil {
		return err
	}

	if err := createDirectories(out, opts.hostBaseDir, opts.dataDir); err != nil {
		return err
	}

	composeFiles := []string{"-f", "docker-compose.yml"}
	if opts.networkMode == "host" {
		composeFiles = append(composeFiles, "-f", "docker-compose.host.yml")
	}
	if opts.buildFromSource {
		composeFiles = append(composeFiles, "-f", "docker-compose.build.yml")
	}

	if opts.buildFromSource {
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleInfo.Render("Building Docker images..."))
		buildID := time.Now().Format("20060102150405")
		if err := compose.runWithEnv(append(composeFiles, "build"), []string{"PUBLIC_BUILD_ID=" + buildID}); err != nil {
			return err
		}
	} else {
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleInfo.Render("Pulling Docker images..."))
		if err := compose.run(append(composeFiles, "pull")); err != nil {
			return err
		}
	}

	fmt.Fprintln(out, styleInfo.Render("Starting services..."))
	installErr := compose.run(append(composeFiles, "up", "-d"))

	// Calculate installation duration
	installDuration := time.Since(installStart)
	installDurationMs := installDuration.Milliseconds()

	// Send installation telemetry and capture telemetry key
	if opts.telemetryEnabled {
		go func() {
			ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
			defer cancel()

			endpoint := "https://mineos.net"
			client := telemetry.NewClient(endpoint, true, "")

			errorMsg := ""
			if installErr != nil {
				errorMsg = installErr.Error()
			}

			version := resolveImageVersion(opts.imageTag)
			if version == "" {
				version = "source"
			}

			event := telemetry.BuildInstallEvent(installationID, version, installErr == nil, installDurationMs, errorMsg)
			resp, err := client.ReportInstall(ctx, event)

			// If we got a telemetry key, append it to .env
			if err == nil && resp != nil && resp.TelemetryKey != "" {
				appendToEnv(".env", "MINEOS_TELEMETRY_KEY", resp.TelemetryKey)
			}
		}()
	}

	if installErr != nil {
		return installErr
	}

	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleBox.Render(styleSuccess.Render("  Installation Complete! 🎉  ")))
	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleSuccess.Render("  Your MineOS server is now running!"))
	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleTitle.Render("  Web Interface"))
	fmt.Fprintf(out, "  %s %s\n", styleLabel.Render("Open your browser:"), styleValue.Render(opts.webOrigin))
	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleTitle.Render("  Login Credentials"))
	fmt.Fprintf(out, "  %s  %s\n", styleLabel.Render("Username:"), styleValue.Render(opts.adminUser))
	fmt.Fprintf(out, "  %s  %s\n", styleLabel.Render("Password:"), styleValue.Render(opts.adminPass))
	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleTitle.Render("  API Information")+" "+styleDim.Render("(for advanced users)"))
	fmt.Fprintf(out, "  %s  %s\n", styleDim.Render("Endpoint:"), styleInfo.Render(fmt.Sprintf("http://localhost:%d", opts.apiPort)))
	fmt.Fprintf(out, "  %s  %s\n", styleDim.Render("Docs:    "), styleInfo.Render(fmt.Sprintf("http://localhost:%d/swagger", opts.apiPort)))
	fmt.Fprintf(out, "  %s  %s\n", styleDim.Render("API key: "), styleInfo.Render(apiKey))
	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleTitle.Render("  Next Steps"))
	fmt.Fprintln(out, styleSuccess.Render("  1.")+" Open the web interface in your browser")
	fmt.Fprintln(out, styleSuccess.Render("  2.")+" Log in with your admin credentials")
	fmt.Fprintln(out, styleSuccess.Render("  3.")+" Create your first Minecraft server!")
	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleDim.Render("  Use the terminal interface for advanced management"))
	fmt.Fprintln(out, styleDim.Render("  Use 'mineos --help' to see all available commands"))
	fmt.Fprintln(out, "")

	// Prompt to install CLI to PATH (skip in quiet mode or if explicitly skipped)
	if !opts.quiet && !opts.skipPathInstall {
		installToPath, err := promptYesNo(reader, out, "Install the 'mineos' command to your system PATH for easy access", true)
		if err != nil {
			return err
		}

		if installToPath {
			if err := installCLIToPath(out); err != nil {
				fmt.Fprintf(out, "%s Could not install to PATH: %v\n", styleWarning.Render("Warning:"), err)
				fmt.Fprintln(out, "You can still use the CLI from this directory.")
				printLocalCLIInstructions(out)
			} else {
				pwd, _ := os.Getwd()
				fmt.Fprintln(out, "")
				fmt.Fprintln(out, styleSuccess.Render("CLI installed to system PATH!"))
				fmt.Fprintln(out, "")
				fmt.Fprintln(out, styleTitle.Render("  To manage your servers from the terminal:"))
				fmt.Fprintf(out, "    %s\n", styleInfo.Render(fmt.Sprintf("cd \"%s\"", pwd)))
				fmt.Fprintf(out, "    %s\n", styleInfo.Render("mineos tui"))
				fmt.Fprintln(out, "")
				fmt.Fprintln(out, styleDim.Render("  Note: You must run commands from this directory (or use --env flag)"))
			}
		} else {
			printLocalCLIInstructions(out)
		}
	} else if !opts.quiet {
		printLocalCLIInstructions(out)
	}

	if !opts.quiet {
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleAccent.Render("  Happy Minecrafting! ⛏"))
	} else {
		fmt.Fprintln(out, styleSuccess.Render("Installation complete."))
	}

	return nil
}

type envConfig struct {
	adminUser        string
	adminPass        string
	jwtSecret        string
	apiKey           string
	hostBaseDir      string
	dataDir          string
	networkMode      string
	buildFromSource  bool
	imageTag         string
	apiPort          int
	webPort          int
	webOrigin        string
	caddySite        string
	minecraftHost    string
	bodySizeLimit    string
	telemetryEnabled bool
	installationID   string
}

func renderEnv(cfg envConfig) string {
	// Optional integrations are configured via web UI settings
	curseforgeLine := "# CurseForge__ApiKey="

	builder := &strings.Builder{}
	builder.WriteString("# Database Configuration\n")
	builder.WriteString("DB_TYPE=sqlite\n")
	builder.WriteString("ConnectionStrings__DefaultConnection=Data Source=/app/data/mineos.db\n\n")
	builder.WriteString("# Authentication\n")
	builder.WriteString(fmt.Sprintf("Auth__SeedUsername=%s\n", cfg.adminUser))
	builder.WriteString(fmt.Sprintf("Auth__SeedPassword=%s\n", cfg.adminPass))
	builder.WriteString(fmt.Sprintf("Auth__JwtSecret=%s\n", cfg.jwtSecret))
	builder.WriteString("Auth__JwtIssuer=mineos\n")
	builder.WriteString("Auth__JwtAudience=mineos\n")
	builder.WriteString("Auth__JwtExpiryHours=24\n\n")
	builder.WriteString("# API Configuration\n")
	builder.WriteString(fmt.Sprintf("ApiKey__SeedKey=%s\n", cfg.apiKey))
	builder.WriteString(fmt.Sprintf("MINEOS_API_KEY=%s\n\n", cfg.apiKey))
	builder.WriteString("# Host Configuration\n")
	builder.WriteString(fmt.Sprintf("HOST_BASE_DIRECTORY=%s\n", cfg.hostBaseDir))
	builder.WriteString(fmt.Sprintf("Host__BaseDirectory=%s\n", containerBaseDir))
	builder.WriteString(fmt.Sprintf("Data__Directory=%s\n", cfg.dataDir))
	builder.WriteString("Host__ServersPathSegment=servers\n")
	builder.WriteString("Host__ProfilesPathSegment=profiles\n")
	builder.WriteString("Host__BackupsPathSegment=backups\n")
	builder.WriteString("Host__ArchivesPathSegment=archives\n")
	builder.WriteString("Host__ImportsPathSegment=imports\n")
	builder.WriteString("Host__OwnerUid=1000\n")
	builder.WriteString("Host__OwnerGid=1000\n\n")
	builder.WriteString("# Docker networking (bridge = isolated, host = required for LAN discovery)\n")
	builder.WriteString(fmt.Sprintf("MINEOS_NETWORK_MODE=%s\n", cfg.networkMode))
	builder.WriteString(fmt.Sprintf("MINEOS_BUILD_FROM_SOURCE=%t\n", cfg.buildFromSource))
	if cfg.imageTag != "" {
		builder.WriteString(fmt.Sprintf("MINEOS_IMAGE_TAG=%s\n", cfg.imageTag))
	}
	builder.WriteString("\n# Optional: CurseForge Integration (configure in web UI Settings > Integrations)\n")
	builder.WriteString(curseforgeLine + "\n\n")
	builder.WriteString("# Ports\n")
	builder.WriteString(fmt.Sprintf("API_PORT=%d\n", cfg.apiPort))
	builder.WriteString(fmt.Sprintf("WEB_PORT=%d\n\n", cfg.webPort))
	builder.WriteString("# Web Origins\n")
	builder.WriteString(fmt.Sprintf("WEB_ORIGIN_PROD=%s\n", cfg.webOrigin))
	builder.WriteString(fmt.Sprintf("PUBLIC_API_BASE_URL=%s\n", cfg.webOrigin))
	builder.WriteString(fmt.Sprintf("ORIGIN=%s\n", cfg.webOrigin))
	builder.WriteString(fmt.Sprintf("CADDY_SITE=%s\n\n", cfg.caddySite))
	builder.WriteString("# Minecraft Server Address\n")
	builder.WriteString(fmt.Sprintf("PUBLIC_MINECRAFT_HOST=%s\n\n", cfg.minecraftHost))
	builder.WriteString("# Web UI Upload Limits\n")
	builder.WriteString(fmt.Sprintf("BODY_SIZE_LIMIT=%s\n\n", cfg.bodySizeLimit))
	builder.WriteString("# Logging\n")
	builder.WriteString("Logging__LogLevel__Default=Information\n")
	builder.WriteString("Logging__LogLevel__Microsoft.AspNetCore=Warning\n\n")
	builder.WriteString("# Telemetry\n")
	if cfg.telemetryEnabled {
		builder.WriteString("MINEOS_TELEMETRY_ENABLED=true\n")
	} else {
		builder.WriteString("MINEOS_TELEMETRY_ENABLED=false\n")
	}
	builder.WriteString("MINEOS_TELEMETRY_ENDPOINT=https://mineos.net\n")
	builder.WriteString(fmt.Sprintf("MINEOS_INSTALLATION_ID=%s\n", cfg.installationID))

	return builder.String()
}

func createDirectories(out io.Writer, hostBaseDir, dataDir string) error {
	fmt.Fprintln(out, styleInfo.Render("Creating directories..."))
	paths := []string{
		filepath.Join(hostBaseDir, "servers"),
		filepath.Join(hostBaseDir, "profiles"),
		filepath.Join(hostBaseDir, "backups"),
		filepath.Join(hostBaseDir, "archives"),
		filepath.Join(hostBaseDir, "imports"),
		dataDir,
	}
	for _, path := range paths {
		if err := os.MkdirAll(path, 0o755); err != nil {
			return err
		}
	}

	if runtime.GOOS == "linux" && isRoot() {
		if err := chownRecursive(hostBaseDir, 1000, 1000); err != nil {
			fmt.Fprintf(out, "%s unable to chown %s: %v\n", styleWarning.Render("Warning:"), hostBaseDir, err)
		}
		if err := chownRecursive(dataDir, 1000, 1000); err != nil {
			fmt.Fprintf(out, "%s unable to chown %s: %v\n", styleWarning.Render("Warning:"), dataDir, err)
		}
	} else if runtime.GOOS == "linux" {
		fmt.Fprintln(out, styleDim.Render("Not running as root; skipping ownership changes (1000:1000)."))
	}

	return nil
}

func chownRecursive(path string, uid, gid int) error {
	return filepath.WalkDir(path, func(target string, d os.DirEntry, err error) error {
		if err != nil {
			return err
		}
		return os.Chown(target, uid, gid)
	})
}

func promptString(_ *bufio.Reader, _ io.Writer, label, defaultValue string) (string, error) {
	if defaultValue != "" {
		fmt.Printf("%s %s: ", styleLabel.Render(label), styleDim.Render("(default: "+defaultValue+")"))
	} else {
		fmt.Printf("%s: ", styleLabel.Render(label))
	}

	scanner := bufio.NewScanner(os.Stdin)
	if !scanner.Scan() {
		if err := scanner.Err(); err != nil {
			return "", err
		}
		// EOF with no error - return default
		return defaultValue, nil
	}
	line := strings.TrimSpace(scanner.Text())
	if line == "" {
		return defaultValue, nil
	}
	return line, nil
}

func promptPassword(out io.Writer, prompt string) (string, error) {
	if term.IsTerminal(int(os.Stdin.Fd())) {
		fmt.Fprint(out, styleLabel.Render(strings.TrimSuffix(prompt, " "))+": ")
		bytes, err := term.ReadPassword(int(os.Stdin.Fd()))
		fmt.Fprintln(out)
		if err != nil {
			return "", err
		}
		return strings.TrimSpace(string(bytes)), nil
	}
	reader := bufio.NewReader(os.Stdin)
	return promptString(reader, out, strings.TrimSuffix(prompt, ":"), "")
}

func promptInt(reader *bufio.Reader, out io.Writer, label string, defaultValue int) (int, error) {
	value, err := promptString(reader, out, label, strconv.Itoa(defaultValue))
	if err != nil {
		return 0, err
	}
	parsed, err := strconv.Atoi(strings.TrimSpace(value))
	if err != nil {
		return 0, fmt.Errorf("invalid number for %s", label)
	}
	return parsed, nil
}

func promptYesNo(_ *bufio.Reader, _ io.Writer, label string, defaultValue bool) (bool, error) {
	defaultLabel := "y/N"
	if defaultValue {
		defaultLabel = "Y/n"
	}
	fmt.Printf("%s %s: ", styleLabel.Render(label), styleDim.Render("("+defaultLabel+")"))

	scanner := bufio.NewScanner(os.Stdin)
	if !scanner.Scan() {
		if err := scanner.Err(); err != nil {
			return false, err
		}
		return defaultValue, nil
	}
	line := strings.TrimSpace(scanner.Text())
	if line == "" {
		return defaultValue, nil
	}
	switch strings.ToLower(line) {
	case "y", "yes":
		return true, nil
	case "n", "no":
		return false, nil
	default:
		return defaultValue, nil
	}
}

func promptRelativePath(reader *bufio.Reader, out io.Writer, label, defaultValue string) (string, error) {
	for {
		value, err := promptString(reader, out, label, defaultValue)
		if err != nil {
			return "", err
		}
		if isValidRelativePath(value) {
			if !strings.HasPrefix(value, "./") && !strings.HasPrefix(value, ".\\") {
				value = "./" + value
			}
			return value, nil
		}
		fmt.Println("Path must be relative to the current directory (no leading /, ~, or ..).")
	}
}

func isValidRelativePath(path string) bool {
	path = strings.TrimSpace(path)
	if path == "" {
		return false
	}
	if strings.HasPrefix(path, "/") || strings.HasPrefix(path, "\\") || strings.HasPrefix(path, "~") {
		return false
	}
	if runtime.GOOS == "windows" && strings.Contains(path, ":") {
		return false
	}
	clean := filepath.Clean(path)
	if clean == "." {
		return false
	}
	if strings.HasPrefix(clean, "..") {
		return false
	}
	return !strings.Contains(clean, ".."+string(filepath.Separator))
}

func randomToken(size int) (string, error) {
	buf := make([]byte, size)
	if _, err := rand.Read(buf); err != nil {
		return "", err
	}
	return base64.StdEncoding.EncodeToString(buf), nil
}

func deriveCaddySite(origin string) string {
	origin = strings.TrimSpace(origin)
	if origin == "" {
		return "http://localhost"
	}
	value := origin
	scheme := ""
	if strings.HasPrefix(value, "http://") {
		scheme = "http"
		value = strings.TrimPrefix(value, "http://")
	} else if strings.HasPrefix(value, "https://") {
		scheme = "https"
		value = strings.TrimPrefix(value, "https://")
	}
	host := value
	if slash := strings.Index(host, "/"); slash >= 0 {
		host = host[:slash]
	}
	if colon := strings.Index(host, ":"); colon >= 0 {
		host = host[:colon]
	}
	if host == "" {
		return "http://localhost"
	}
	if scheme == "https" {
		return host
	}
	return "http://" + host
}

func ensureDockerAvailable() error {
	if _, err := exec.LookPath("docker"); err != nil {
		msg := "Docker is not installed.\n\n"
		msg += "MineOS requires Docker to run. Please install Docker Desktop:\n"
		if runtime.GOOS == "windows" {
			msg += "  https://docs.docker.com/desktop/install/windows-install/\n"
		} else if runtime.GOOS == "darwin" {
			msg += "  https://docs.docker.com/desktop/install/mac-install/\n"
		} else {
			msg += "  https://docs.docker.com/engine/install/\n"
		}
		msg += "\nThen re-run this installer."
		return errors.New(msg)
	}
	return nil
}

func ensureDockerRunning() error {
	cmd := exec.Command("docker", "info")
	if err := cmd.Run(); err != nil {
		msg := "Docker is installed but not running.\n\n"
		if runtime.GOOS == "windows" {
			msg += "Please start Docker Desktop from the Start menu or system tray,\n"
			msg += "wait for it to finish loading, then re-run this installer.\n"
		} else if runtime.GOOS == "darwin" {
			msg += "Please start Docker Desktop from Applications,\n"
			msg += "wait for it to finish loading, then re-run this installer.\n"
		} else {
			msg += "Please start the Docker daemon:\n"
			msg += "  sudo systemctl start docker\n"
			msg += "\nThen re-run this installer.\n"
		}
		return errors.New(msg)
	}
	return nil
}

func isPreviewTag(tag string) bool {
	t := strings.ToLower(tag)
	return t == "preview" || strings.Contains(t, "-beta") || strings.Contains(t, "-alpha") || strings.Contains(t, "-rc")
}

func dirExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && info.IsDir()
}

// reuse uninstall compose helper
func (c composeRunner) run(args []string) error {
	cmd := exec.Command(c.exe, append(c.baseArgs, args...)...)
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr
	cmd.Stdin = os.Stdin
	return cmd.Run()
}

func (c composeRunner) runWithEnv(args []string, env []string) error {
	cmd := exec.Command(c.exe, append(c.baseArgs, args...)...)
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr
	cmd.Stdin = os.Stdin
	cmd.Env = append(os.Environ(), env...)
	return cmd.Run()
}

func printLocalCLIInstructions(out io.Writer) {
	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleTitle.Render("  To manage your servers from the terminal:"))

	if runtime.GOOS == "windows" {
		pwd, _ := os.Getwd()
		fmt.Fprintf(out, "    %s\n", styleInfo.Render(fmt.Sprintf("cd \"%s\"", pwd)))
		fmt.Fprintf(out, "    %s\n", styleInfo.Render(".\\mineos.exe tui"))
	} else {
		pwd, _ := os.Getwd()
		fmt.Fprintf(out, "    %s\n", styleInfo.Render(fmt.Sprintf("cd \"%s\"", pwd)))
		fmt.Fprintf(out, "    %s\n", styleInfo.Render("./mineos tui"))
	}

	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleDim.Render("  (Or run the installer again and choose to install to PATH)"))
}

func installCLIToPath(out io.Writer) error {
	// Get current executable path
	exePath, err := os.Executable()
	if err != nil {
		return fmt.Errorf("failed to get executable path: %w", err)
	}

	if runtime.GOOS == "windows" {
		return installCLIToPathWindows(out, exePath)
	}
	return installCLIToPathUnix(out, exePath)
}

func installCLIToPathWindows(out io.Writer, exePath string) error {
	// Install to %LOCALAPPDATA%\Programs\MineOS\mineos.exe
	localAppData := os.Getenv("LOCALAPPDATA")
	if localAppData == "" {
		return errors.New("LOCALAPPDATA environment variable not set")
	}

	installDir := filepath.Join(localAppData, "Programs", "MineOS")
	if err := os.MkdirAll(installDir, 0o755); err != nil {
		return fmt.Errorf("failed to create install directory: %w", err)
	}

	destPath := filepath.Join(installDir, "mineos.exe")

	// Copy executable
	if err := copyFile(exePath, destPath); err != nil {
		return fmt.Errorf("failed to copy executable: %w", err)
	}

	fmt.Fprintf(out, "%s %s\n", styleSuccess.Render("Installed to:"), styleValue.Render(destPath))

	// Show important note about .env location
	pwd, _ := os.Getwd()
	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleWarning.Render("IMPORTANT:")+" Your .env file is located at:")
	fmt.Fprintf(out, "  %s\n", styleValue.Render(pwd+"\\.env"))
	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleDim.Render("You must either:"))
	fmt.Fprintf(out, "  %s cd to this directory before running mineos commands, OR\n", styleSuccess.Render("1."))
	fmt.Fprintf(out, "  %s Use the --env flag: %s\n", styleSuccess.Render("2."), styleInfo.Render(fmt.Sprintf("mineos --env \"%s\\.env\" tui", pwd)))

	// Check if directory is in PATH
	pathEnv := os.Getenv("PATH")
	if !strings.Contains(strings.ToLower(pathEnv), strings.ToLower(installDir)) {
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleWarning.Render("⚠  Almost done!")+" The installation directory is not in your PATH.")
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleDim.Render("To complete the installation, add this directory to your PATH:"))
		fmt.Fprintf(out, "  %s\n", styleValue.Render(installDir))
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleTitle.Render("How to add to PATH:"))
		fmt.Fprintf(out, "  %s Press Win+R, type %s, press Enter\n", styleDim.Render("1."), styleInfo.Render("sysdm.cpl"))
		fmt.Fprintf(out, "  %s Go to 'Advanced' tab > 'Environment Variables'\n", styleDim.Render("2."))
		fmt.Fprintf(out, "  %s Under 'User variables', select 'Path' > 'Edit'\n", styleDim.Render("3."))
		fmt.Fprintf(out, "  %s Click 'New' and paste the directory path above\n", styleDim.Render("4."))
		fmt.Fprintf(out, "  %s Click 'OK' on all windows\n", styleDim.Render("5."))
		fmt.Fprintf(out, "  %s Restart your terminal\n", styleDim.Render("6."))
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleDim.Render("Alternatively, use PowerShell (run as user, not admin):"))
		fmt.Fprintf(out, "  %s\n", styleInfo.Render("$env:Path += ';"+installDir+"'"))
		fmt.Fprintf(out, "  %s\n", styleInfo.Render("[Environment]::SetEnvironmentVariable('Path', $env:Path, 'User')"))
	}

	return nil
}

func installCLIToPathUnix(out io.Writer, exePath string) error {
	// Try to install to /usr/local/bin (requires sudo) or ~/.local/bin
	var destPath string
	var installDir string

	// Check if we have write access to /usr/local/bin
	if runtime.GOOS == "linux" || runtime.GOOS == "darwin" {
		systemBin := "/usr/local/bin/mineos"
		if err := testWriteAccess("/usr/local/bin"); err == nil {
			destPath = systemBin
			installDir = "/usr/local/bin"
		}
	}

	// Fall back to user's local bin
	if destPath == "" {
		homeDir, err := os.UserHomeDir()
		if err != nil {
			return fmt.Errorf("failed to get home directory: %w", err)
		}
		installDir = filepath.Join(homeDir, ".local", "bin")
		if err := os.MkdirAll(installDir, 0o755); err != nil {
			return fmt.Errorf("failed to create install directory: %w", err)
		}
		destPath = filepath.Join(installDir, "mineos")
	}

	// Copy and make executable
	if err := copyFile(exePath, destPath); err != nil {
		return fmt.Errorf("failed to copy executable: %w", err)
	}
	if err := os.Chmod(destPath, 0o755); err != nil {
		return fmt.Errorf("failed to make executable: %w", err)
	}

	fmt.Fprintf(out, "%s %s\n", styleSuccess.Render("Installed to:"), styleValue.Render(destPath))

	// Show important note about .env location
	pwd, _ := os.Getwd()
	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleWarning.Render("IMPORTANT:")+" Your .env file is located at:")
	fmt.Fprintf(out, "  %s\n", styleValue.Render(pwd+"/.env"))
	fmt.Fprintln(out, "")
	fmt.Fprintln(out, styleDim.Render("You must either:"))
	fmt.Fprintf(out, "  %s cd to this directory before running mineos commands, OR\n", styleSuccess.Render("1."))
	fmt.Fprintf(out, "  %s Use the --env flag: %s\n", styleSuccess.Render("2."), styleInfo.Render(fmt.Sprintf("mineos --env \"%s/.env\" tui", pwd)))

	// Check if directory is in PATH
	pathEnv := os.Getenv("PATH")
	if !strings.Contains(pathEnv, installDir) {
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleWarning.Render("⚠  Almost done!")+" The installation directory is not in your PATH.")
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleDim.Render("Add this line to your shell profile (~/.bashrc, ~/.zshrc, or ~/.profile):"))
		fmt.Fprintf(out, "  %s\n", styleInfo.Render(fmt.Sprintf("export PATH=\"%s:$PATH\"", installDir)))
		fmt.Fprintln(out, "")
		fmt.Fprintln(out, styleDim.Render("Then reload your shell:"))
		fmt.Fprintf(out, "  %s\n", styleInfo.Render("source ~/.bashrc  # or ~/.zshrc"))
	}

	return nil
}

func testWriteAccess(dir string) error {
	testFile := filepath.Join(dir, ".write_test_"+strconv.FormatInt(time.Now().UnixNano(), 10))
	f, err := os.Create(testFile)
	if err != nil {
		return err
	}
	f.Close()
	return os.Remove(testFile)
}

// resolveImageVersion inspects the pulled Docker image to get the actual version
// from OCI labels, rather than using channel names like "latest" or "preview".
func resolveImageVersion(imageTag string) string {
	if imageTag == "" {
		return ""
	}
	// If user specified a concrete version (not a channel alias), use it directly
	tag := strings.ToLower(imageTag)
	if tag != "latest" && tag != "preview" && tag != "edge" {
		return imageTag
	}
	// Try to read the actual version from the pulled image's OCI labels
	imageName := fmt.Sprintf("ghcr.io/freeman412/mineos-api:%s", imageTag)
	out, err := exec.Command("docker", "inspect", "--format",
		`{{index .Config.Labels "org.opencontainers.image.version"}}`, imageName).Output()
	if err == nil {
		version := strings.TrimSpace(string(out))
		if version != "" && version != "<no value>" {
			return version
		}
	}
	return imageTag
}

// appendToEnv appends a KEY=VALUE line to the given .env file.
func appendToEnv(path, key, value string) {
	f, err := os.OpenFile(path, os.O_APPEND|os.O_WRONLY, 0o644)
	if err != nil {
		return
	}
	defer f.Close()
	fmt.Fprintf(f, "\n%s=%s\n", key, value)
}
