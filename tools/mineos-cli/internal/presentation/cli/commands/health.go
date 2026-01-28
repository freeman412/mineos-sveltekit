package commands

import (
	"context"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

func NewHealthCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:   "health",
		Short: "Check MineOS API health",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := context.Background()
			cfg, err := loadConfig.Execute(ctx)
			if err != nil {
				return err
			}
			client := api.NewClientFromConfig(cfg)
			uc := usecases.NewHealthCheckUseCase(client)
			if err := uc.Execute(ctx); err != nil {
				return err
			}
			cmd.Println("OK")
			return nil
		},
	}
}
