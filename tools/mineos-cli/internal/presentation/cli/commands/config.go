package commands

import (
	"context"
	"fmt"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
)

func NewConfigCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	cmd := &cobra.Command{
		Use:   "config",
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

			return nil
		},
	}

	return cmd
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
