package commands

import (
	"context"
	"fmt"
	"os"
	"strings"

	"github.com/joho/godotenv"
	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
)

func NewConfigCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	showCmd := newConfigShowCommand(loadConfig)

	cmd := &cobra.Command{
		Use:   "config",
		Short: "Manage MineOS configuration",
		RunE:  showCmd.RunE, // Default to showing config if no subcommand
	}

	cmd.AddCommand(showCmd)
	cmd.AddCommand(newConfigSetUpdateChannelCommand(loadConfig))

	return cmd
}

func newConfigShowCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:   "show",
		Short: "Show resolved configuration",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := context.Background()
			cfg, err := loadConfig.Execute(ctx)
			if err != nil {
				return err
			}

			fmt.Printf("Env file: %s\n", cfg.EnvPath)
			fmt.Printf("API port: %s\n", fallback(cfg.ApiPort, "5078"))
			fmt.Printf("Web origin: %s\n", fallback(cfg.WebOrigin, "http://localhost:3000"))
			fmt.Printf("Network mode: %s\n", fallback(cfg.NetworkMode, "bridge"))
			fmt.Printf("Build from source: %s\n", fallback(cfg.BuildFromSource, "false"))
			fmt.Printf("Image tag: %s\n", fallback(cfg.ImageTag, "latest"))
			fmt.Printf("Minecraft host: %s\n", fallback(cfg.MinecraftHost, "localhost"))
			fmt.Printf("Data directory: %s\n", fallback(cfg.DataDirectory, "./data"))
			fmt.Printf("Shutdown timeout: %s\n", fallback(cfg.ShutdownTimeout, "300"))
			fmt.Printf("DB type: %s\n", fallback(cfg.DatabaseType, "sqlite"))
			fmt.Printf("DB connection: %s\n", mask(cfg.DatabaseConnection))
			fmt.Printf("Management API key: %s\n", mask(cfg.ManagementApiKey))
			fmt.Printf("Static API key: %s\n", mask(cfg.ApiKeyStatic))
			fmt.Printf("Seed API key: %s\n", mask(cfg.ApiKeySeed))

			updateChannel := "stable"
			if cfg.IsPreReleaseEnabled() {
				updateChannel = "pre-release"
			}
			fmt.Printf("CLI update channel: %s\n", updateChannel)

			return nil
		},
	}
}

func newConfigSetUpdateChannelCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:   "set-update-channel {stable|prerelease}",
		Short: "Set the CLI update channel (stable or pre-release)",
		Long: `Set the update channel for CLI updates.

Channels:
  stable      - Only stable releases (default)
  prerelease  - Include beta and pre-release versions

Examples:
  mineos config set-update-channel stable
  mineos config set-update-channel prerelease`,
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			ctx := context.Background()
			cfg, err := loadConfig.Execute(ctx)
			if err != nil {
				return err
			}

			channel := strings.ToLower(args[0])
			var value string

			switch channel {
			case "stable":
				value = "false"
			case "prerelease", "pre-release":
				value = "true"
			default:
				return fmt.Errorf("invalid channel: %s (must be 'stable' or 'prerelease')", args[0])
			}

			// Update .env file
			envMap, err := godotenv.Read(cfg.EnvPath)
			if err != nil && !os.IsNotExist(err) {
				return fmt.Errorf("failed to read .env: %w", err)
			}
			if envMap == nil {
				envMap = make(map[string]string)
			}

			envMap["MINEOS_CLI_PRERELEASE_UPDATES"] = value

			if err := godotenv.Write(envMap, cfg.EnvPath); err != nil {
				return fmt.Errorf("failed to write .env: %w", err)
			}

			channelName := "stable"
			if value == "true" {
				channelName = "pre-release"
			}

			fmt.Printf("✓ Update channel set to: %s\n", channelName)
			fmt.Println("\nRun 'mineos upgrade' to check for updates on this channel.")

			return nil
		},
	}
}

func fallback(value, fallbackValue string) string {
	if value == "" {
		return fallbackValue
	}
	return value
}

func mask(value string) string {
	if value == "" {
		return "(empty)"
	}
	if len(value) <= 6 {
		return "***"
	}
	return value[:3] + "***" + value[len(value)-3:]
}
