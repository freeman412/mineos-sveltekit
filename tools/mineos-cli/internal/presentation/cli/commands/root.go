package commands

import (
	"github.com/spf13/cobra"
	"go.uber.org/zap"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/ports"
)

type RootDeps struct {
	ConfigRepo ports.ConfigRepository
	LoadConfig *usecases.LoadConfigUseCase
	Logger     *zap.Logger
	Version    string
}

func NewRootCommand(deps RootDeps) *cobra.Command {
	var envPath string

	cmd := &cobra.Command{
		Use:   "mineos",
		Short: "MineOS management CLI",
		Long:  "MineOS management CLI for server setup and operations.",
		PersistentPreRunE: func(cmd *cobra.Command, _ []string) error {
			if envPath != "" {
				deps.ConfigRepo.SetPath(envPath)
			}
			return nil
		},
	}

	cmd.PersistentFlags().StringVar(&envPath, "env", ".env", "Path to the MineOS .env file")

	cmd.AddCommand(NewConfigCommand(deps.LoadConfig))
	cmd.AddCommand(NewHealthCommand(deps.LoadConfig))
	cmd.AddCommand(NewInteractiveCommand(deps.LoadConfig))
	cmd.AddCommand(NewTuiCommand(deps.LoadConfig))
	cmd.AddCommand(NewServersCommand(deps.LoadConfig))
	cmd.AddCommand(NewStatusCommand(deps.LoadConfig))
	cmd.AddCommand(NewUninstallCommand())
	cmd.AddCommand(NewVersionCommand(deps.Version))

	return cmd
}
