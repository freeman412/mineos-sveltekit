package commands

import (
	"context"
	"fmt"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

func NewStatusCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:   "status",
		Short: "Show installation and service status",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := context.Background()
			cfg, err := loadConfig.Execute(ctx)
			if err != nil {
				return err
			}
			client := api.NewClientFromConfig(cfg)
			health := "unhealthy"
			if err := client.Health(ctx); err == nil {
				health = "healthy"
			}
			fmt.Printf("API: %s\n", health)
			fmt.Printf("Web origin: %s\n", fallback(cfg.WebOrigin, "http://localhost:3000"))
			fmt.Printf("Minecraft host: %s\n", fallback(cfg.MinecraftHost, "localhost"))
			fmt.Printf("Network mode: %s\n", fallback(cfg.NetworkMode, "bridge"))
			return nil
		},
	}
}
