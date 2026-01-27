package commands

import (
	"bufio"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"runtime"
	"strconv"
	"strings"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
)

func NewReconfigureCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:   "reconfigure",
		Short: "Update MineOS configuration in .env",
		RunE: func(cmd *cobra.Command, _ []string) error {
			return runReconfigure(cmd, loadConfig)
		},
	}
}

func runReconfigure(cmd *cobra.Command, loadConfig *usecases.LoadConfigUseCase) error {
	ctx := cmd.Context()
	out := cmd.OutOrStdout()
	var reader *bufio.Reader // kept for function signature compatibility

	cfg, err := loadConfig.Execute(ctx)
	if err != nil {
		if os.IsNotExist(err) {
			return errors.New("no .env found; run `mineos install` first")
		}
		return err
	}
	envPath := cfg.EnvPath
	if envPath == "" {
		envPath = ".env"
	}

	values, err := loadEnvValues(envPath)
	if err != nil {
		return err
	}

	currentAdmin := fallback(values["Auth__SeedUsername"], "admin")
	currentPass := values["Auth__SeedPassword"]
	currentHostDir := fallback(values["HOST_BASE_DIRECTORY"], defaultHostBaseDir)
	currentDataDir := fallback(values["Data__Directory"], defaultDataDir)
	currentApiPort := parseEnvInt(values["API_PORT"], defaultApiPort)
	currentWebPort := parseEnvInt(values["WEB_PORT"], defaultWebPort)

	currentOrigin := values["WEB_ORIGIN_PROD"]
	if currentOrigin == "" {
		currentOrigin = values["ORIGIN"]
	}
	if currentOrigin == "" {
		currentOrigin = fmt.Sprintf("http://localhost:%d", currentWebPort)
	}

	currentMinecraftHost := fallback(values["PUBLIC_MINECRAFT_HOST"], "localhost")
	currentBodySize := fallback(values["BODY_SIZE_LIMIT"], defaultBodySizeLimit)
	currentCurseforge := values["CurseForge__ApiKey"]
	currentDiscord := values["Discord__WebhookUrl"]
	currentNetworkMode := fallback(values["MINEOS_NETWORK_MODE"], defaultNetworkMode)
	currentBuildFromSource := parseEnvBool(values["MINEOS_BUILD_FROM_SOURCE"])
	currentImageTag := fallback(values["MINEOS_IMAGE_TAG"], "latest")
	currentManagementKey := values["MINEOS_API_KEY"]

	fmt.Fprintln(out, "MineOS reconfigure")
	fmt.Fprintln(out, "Press Enter to keep the current value.")

	adminUser, err := promptString(reader, out, "Admin username", currentAdmin)
	if err != nil {
		return err
	}

	adminPass, err := promptOptionalPassword(out, currentPass)
	if err != nil {
		return err
	}

	managementKey, err := promptOptionalValue(reader, out, "Management API key for this CLI", currentManagementKey)
	if err != nil {
		return err
	}

	hostDir, err := promptRelativePathWithCurrent(reader, out, "Local storage directory for Minecraft servers (relative)", currentHostDir)
	if err != nil {
		return err
	}

	dataDir, err := promptRelativePathWithCurrent(reader, out, "Database directory (relative)", currentDataDir)
	if err != nil {
		return err
	}

	apiPort, err := promptInt(reader, out, "API port", currentApiPort)
	if err != nil {
		return err
	}

	webPort, err := promptInt(reader, out, "Web UI port", currentWebPort)
	if err != nil {
		return err
	}

	webOrigin, err := promptString(reader, out, "Web UI origin", currentOrigin)
	if err != nil {
		return err
	}
	caddySite := deriveCaddySite(webOrigin)

	minecraftHost, err := promptString(reader, out, "Public Minecraft host", currentMinecraftHost)
	if err != nil {
		return err
	}

	bodySizeLimit, err := promptString(reader, out, "Web UI upload body size limit", currentBodySize)
	if err != nil {
		return err
	}

	fmt.Fprintln(out, "")
	fmt.Fprintln(out, "LAN discovery requires host networking on Linux.")
	enableHostNetworking, err := promptYesNo(reader, out, "Enable host networking for LAN discovery", currentNetworkMode == "host")
	if err != nil {
		return err
	}
	networkMode := defaultNetworkMode
	if enableHostNetworking {
		if runtime.GOOS != "linux" {
			fmt.Fprintln(out, "Host networking is only supported on Linux. Using bridge mode.")
		} else {
			networkMode = "host"
		}
	}

	buildFromSource, err := promptYesNo(reader, out, "Build images from source instead of pulling", currentBuildFromSource)
	if err != nil {
		return err
	}

	imageTag, err := promptString(reader, out, "Image tag to pull", currentImageTag)
	if err != nil {
		return err
	}

	curseforgeKey, err := promptOptionalValue(reader, out, "CurseForge API key (optional)", currentCurseforge)
	if err != nil {
		return err
	}

	discordWebhook, err := promptOptionalValue(reader, out, "Discord webhook URL (optional)", currentDiscord)
	if err != nil {
		return err
	}

	if err := setEnvFileValue(envPath, "Auth__SeedUsername", adminUser); err != nil {
		return err
	}
	if strings.TrimSpace(adminPass) != "" {
		if err := setEnvFileValue(envPath, "Auth__SeedPassword", adminPass); err != nil {
			return err
		}
	}
	if strings.TrimSpace(managementKey) != "" {
		if err := setEnvFileValue(envPath, "MINEOS_API_KEY", managementKey); err != nil {
			return err
		}
	}
	if err := setEnvFileValue(envPath, "HOST_BASE_DIRECTORY", hostDir); err != nil {
		return err
	}
	if err := setEnvFileValue(envPath, "Data__Directory", dataDir); err != nil {
		return err
	}
	if err := setEnvFileValue(envPath, "API_PORT", strconv.Itoa(apiPort)); err != nil {
		return err
	}
	if err := setEnvFileValue(envPath, "WEB_PORT", strconv.Itoa(webPort)); err != nil {
		return err
	}
	if err := setEnvFileValue(envPath, "WEB_ORIGIN_PROD", webOrigin); err != nil {
		return err
	}
	if err := setEnvFileValue(envPath, "PUBLIC_API_BASE_URL", webOrigin); err != nil {
		return err
	}
	if err := setEnvFileValue(envPath, "ORIGIN", webOrigin); err != nil {
		return err
	}
	if err := setEnvFileValue(envPath, "CADDY_SITE", caddySite); err != nil {
		return err
	}
	if err := setEnvFileValue(envPath, "PUBLIC_MINECRAFT_HOST", minecraftHost); err != nil {
		return err
	}
	if err := setEnvFileValue(envPath, "BODY_SIZE_LIMIT", bodySizeLimit); err != nil {
		return err
	}
	if err := setEnvFileValue(envPath, "MINEOS_NETWORK_MODE", networkMode); err != nil {
		return err
	}
	if err := setEnvFileValue(envPath, "MINEOS_BUILD_FROM_SOURCE", strconv.FormatBool(buildFromSource)); err != nil {
		return err
	}
	if err := setEnvFileValue(envPath, "MINEOS_IMAGE_TAG", imageTag); err != nil {
		return err
	}
	if strings.TrimSpace(curseforgeKey) != "" {
		if err := setEnvFileValue(envPath, "CurseForge__ApiKey", curseforgeKey); err != nil {
			return err
		}
	}
	if strings.TrimSpace(discordWebhook) != "" {
		if err := setEnvFileValue(envPath, "Discord__WebhookUrl", discordWebhook); err != nil {
			return err
		}
	}

	fmt.Println("Configuration updated.")

	// Ask if user wants to restart services
	restartServices, err := promptYesNo(nil, nil, "Restart services now to apply changes", true)
	if err != nil {
		return err
	}

	if restartServices {
		fmt.Println("Restarting services...")
		compose, composeErr := detectCompose()
		if composeErr != nil {
			fmt.Println("Warning: Could not detect docker compose. Please restart services manually.")
			return nil
		}

		// Run docker compose restart
		args := append([]string{}, compose.baseArgs...)
		args = append(args, "restart")
		restartCmd := exec.Command(compose.exe, args...)
		restartCmd.Stdout = os.Stdout
		restartCmd.Stderr = os.Stderr
		if err := restartCmd.Run(); err != nil {
			return fmt.Errorf("failed to restart services: %w", err)
		}
		fmt.Println("Services restarted successfully.")
	}

	return nil
}

func parseEnvInt(raw string, fallbackValue int) int {
	raw = strings.TrimSpace(raw)
	if raw == "" {
		return fallbackValue
	}
	parsed, err := strconv.Atoi(raw)
	if err != nil {
		return fallbackValue
	}
	return parsed
}

func parseEnvBool(raw string) bool {
	parsed, err := strconv.ParseBool(strings.TrimSpace(raw))
	return err == nil && parsed
}

func promptOptionalPassword(out io.Writer, current string) (string, error) {
	value, err := promptPassword(out, "Admin password (leave blank to keep current): ")
	if err != nil {
		return "", err
	}
	if strings.TrimSpace(value) == "" {
		return current, nil
	}
	return value, nil
}

func promptOptionalValue(_ *bufio.Reader, _ io.Writer, label, current string) (string, error) {
	if current != "" {
		fmt.Printf("%s (leave blank to keep current): ", label)
	} else {
		fmt.Printf("%s (optional): ", label)
	}

	scanner := bufio.NewScanner(os.Stdin)
	if !scanner.Scan() {
		if err := scanner.Err(); err != nil {
			return "", err
		}
		return current, nil
	}
	line := strings.TrimSpace(scanner.Text())
	if line == "" {
		return current, nil
	}
	return line, nil
}

func promptRelativePathWithCurrent(reader *bufio.Reader, out io.Writer, label, current string) (string, error) {
	for {
		value, err := promptString(reader, out, label, current)
		if err != nil {
			return "", err
		}
		value = strings.TrimSpace(value)
		if value == "" || value == current {
			return current, nil
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
