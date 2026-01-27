package commands

import (
	"bufio"
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

	"github.com/spf13/cobra"
	"golang.org/x/term"
)

type installOptions struct {
	adminUser       string
	adminPass       string
	apiKey          string
	hostBaseDir     string
	dataDir         string
	apiPort         int
	webPort         int
	webOrigin       string
	minecraftHost   string
	bodySizeLimit   string
	networkMode     string
	buildFromSource bool
	imageTag        string
	curseforgeKey   string
	discordWebhook  string
}

const (
	defaultHostBaseDir   = "./minecraft"
	defaultDataDir       = "./data"
	defaultNetworkMode   = "bridge"
	defaultApiPort       = 5078
	defaultWebPort       = 3000
	defaultBodySizeLimit = "Infinity"
	containerBaseDir     = "/var/games/minecraft"
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
	cmd.Flags().StringVar(&opts.curseforgeKey, "curseforge-key", "", "CurseForge API key (optional)")
	cmd.Flags().StringVar(&opts.discordWebhook, "discord-webhook", "", "Discord webhook URL (optional)")

	return cmd
}

func runInstall(cmd *cobra.Command, opts installOptions) error {
	out := cmd.OutOrStdout()
	var reader *bufio.Reader // kept for function signature compatibility

	if err := ensureDockerAvailable(); err != nil {
		return err
	}

	compose, err := detectCompose()
	if err != nil {
		return err
	}

	if _, err := os.Stat(".env"); err == nil {
		overwrite, err := promptYesNo(reader, out, ".env already exists. Overwrite", false)
		if err != nil {
			return err
		}
		if !overwrite {
			return errors.New("install cancelled")
		}
	}

	fmt.Fprintln(out, "MineOS installer")
	fmt.Fprintln(out, "Press Enter to accept defaults.")

	if opts.adminUser == "" {
		value, err := promptString(reader, out, "Admin username", "admin")
		if err != nil {
			return err
		}
		opts.adminUser = value
	}

	if opts.adminPass == "" {
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

	if opts.hostBaseDir == "" {
		value, err := promptRelativePath(reader, out, "Local storage directory for Minecraft servers (relative)", defaultHostBaseDir)
		if err != nil {
			return err
		}
		opts.hostBaseDir = value
	} else if !isValidRelativePath(opts.hostBaseDir) {
		return fmt.Errorf("host-dir must be relative (no leading /, ~, or ..)")
	}

	if opts.dataDir == "" {
		value, err := promptRelativePath(reader, out, "Database directory (relative)", defaultDataDir)
		if err != nil {
			return err
		}
		opts.dataDir = value
	} else if !isValidRelativePath(opts.dataDir) {
		return fmt.Errorf("data-dir must be relative (no leading /, ~, or ..)")
	}

	if opts.apiPort == 0 {
		value, err := promptInt(reader, out, "API port", defaultApiPort)
		if err != nil {
			return err
		}
		opts.apiPort = value
	}

	if opts.webPort == 0 {
		value, err := promptInt(reader, out, "Web UI port", defaultWebPort)
		if err != nil {
			return err
		}
		opts.webPort = value
	}

	if opts.webOrigin == "" {
		defaultOrigin := fmt.Sprintf("http://localhost:%d", opts.webPort)
		value, err := promptString(reader, out, "Web UI origin", defaultOrigin)
		if err != nil {
			return err
		}
		opts.webOrigin = value
	}

	if opts.minecraftHost == "" {
		value, err := promptString(reader, out, "Public Minecraft host", "localhost")
		if err != nil {
			return err
		}
		opts.minecraftHost = value
	}

	if opts.bodySizeLimit == "" {
		value, err := promptString(reader, out, "Web UI upload body size limit", defaultBodySizeLimit)
		if err != nil {
			return err
		}
		opts.bodySizeLimit = value
	}

	networkModeChanged := cmd.Flags().Changed("network-mode")
	if !networkModeChanged {
		if runtime.GOOS != "linux" {
			opts.networkMode = defaultNetworkMode
		} else {
			fmt.Fprintln(out, "")
			fmt.Fprintln(out, "LAN discovery requires host networking on Linux.")
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
	} else {
		mode := strings.ToLower(strings.TrimSpace(opts.networkMode))
		if mode != "bridge" && mode != "host" {
			return fmt.Errorf("invalid network-mode: %s", opts.networkMode)
		}
		if mode == "host" && runtime.GOOS != "linux" {
			fmt.Fprintln(out, "Host networking is only supported on Linux. Using bridge mode.")
			mode = defaultNetworkMode
		}
		opts.networkMode = mode
	}

	buildChanged := cmd.Flags().Changed("build")
	if !buildChanged {
		value, err := promptYesNo(reader, out, "Build images from source instead of pulling", false)
		if err != nil {
			return err
		}
		opts.buildFromSource = value
	}

	if !opts.buildFromSource {
		if opts.imageTag == "" {
			value, err := promptString(reader, out, "Image tag to pull", "latest")
			if err != nil {
				return err
			}
			opts.imageTag = value
		}
	} else if !dirExists("apps") {
		return errors.New("source files not found (./apps missing); use the installer with --build after cloning the repo")
	}

	if opts.curseforgeKey == "" {
		value, err := promptString(reader, out, "CurseForge API key (optional)", "")
		if err != nil {
			return err
		}
		opts.curseforgeKey = value
	}

	if opts.discordWebhook == "" {
		value, err := promptString(reader, out, "Discord webhook URL (optional)", "")
		if err != nil {
			return err
		}
		opts.discordWebhook = value
	}

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
		adminUser:       opts.adminUser,
		adminPass:       opts.adminPass,
		jwtSecret:       jwtSecret,
		apiKey:          apiKey,
		hostBaseDir:     opts.hostBaseDir,
		dataDir:         opts.dataDir,
		networkMode:     opts.networkMode,
		buildFromSource: opts.buildFromSource,
		imageTag:        opts.imageTag,
		apiPort:         opts.apiPort,
		webPort:         opts.webPort,
		webOrigin:       opts.webOrigin,
		caddySite:       caddySite,
		minecraftHost:   opts.minecraftHost,
		bodySizeLimit:   opts.bodySizeLimit,
		curseforgeKey:   opts.curseforgeKey,
		discordWebhook:  opts.discordWebhook,
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
		fmt.Fprintln(out, "Building Docker images...")
		buildID := time.Now().Format("20060102150405")
		if err := compose.runWithEnv(append(composeFiles, "build"), []string{"PUBLIC_BUILD_ID=" + buildID}); err != nil {
			return err
		}
	} else {
		fmt.Fprintln(out, "Pulling Docker images...")
		if err := compose.run(append(composeFiles, "pull")); err != nil {
			return err
		}
	}

	fmt.Fprintln(out, "Starting services...")
	if err := compose.run(append(composeFiles, "up", "-d")); err != nil {
		return err
	}

	fmt.Fprintln(out, "Install complete.")
	fmt.Fprintf(out, "Web UI: %s\n", opts.webOrigin)
	fmt.Fprintf(out, "API: http://localhost:%d\n", opts.apiPort)
	fmt.Fprintf(out, "API Docs: http://localhost:%d/swagger\n", opts.apiPort)
	fmt.Fprintln(out, "Admin username:", opts.adminUser)
	fmt.Fprintln(out, "Admin password:", opts.adminPass)
	fmt.Fprintln(out, "API key:", apiKey)

	return nil
}

type envConfig struct {
	adminUser       string
	adminPass       string
	jwtSecret       string
	apiKey          string
	hostBaseDir     string
	dataDir         string
	networkMode     string
	buildFromSource bool
	imageTag        string
	apiPort         int
	webPort         int
	webOrigin       string
	caddySite       string
	minecraftHost   string
	bodySizeLimit   string
	curseforgeKey   string
	discordWebhook  string
}

func renderEnv(cfg envConfig) string {
	curseforgeLine := "# CurseForge__ApiKey="
	if strings.TrimSpace(cfg.curseforgeKey) != "" {
		curseforgeLine = "CurseForge__ApiKey=" + cfg.curseforgeKey
	}
	discordLine := "# Discord__WebhookUrl="
	if strings.TrimSpace(cfg.discordWebhook) != "" {
		discordLine = "Discord__WebhookUrl=" + cfg.discordWebhook
	}

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
	builder.WriteString("\n# Optional: CurseForge Integration\n")
	builder.WriteString(curseforgeLine + "\n\n")
	builder.WriteString("# Optional: Discord Integration\n")
	builder.WriteString(discordLine + "\n\n")
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
	builder.WriteString("Logging__LogLevel__Microsoft.AspNetCore=Warning\n")

	return builder.String()
}

func createDirectories(out io.Writer, hostBaseDir, dataDir string) error {
	fmt.Fprintln(out, "Creating directories...")
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
			fmt.Fprintf(out, "Warning: unable to chown %s: %v\n", hostBaseDir, err)
		}
		if err := chownRecursive(dataDir, 1000, 1000); err != nil {
			fmt.Fprintf(out, "Warning: unable to chown %s: %v\n", dataDir, err)
		}
	} else if runtime.GOOS == "linux" {
		fmt.Fprintln(out, "Not running as root; skipping ownership changes (1000:1000).")
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
		fmt.Printf("%s (default: %s): ", label, defaultValue)
	} else {
		fmt.Printf("%s: ", label)
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
		fmt.Fprint(out, prompt)
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
	fmt.Printf("%s (%s): ", label, defaultLabel)

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
		return errors.New("docker is not installed")
	}
	return nil
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
