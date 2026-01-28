package commands

import (
	"github.com/spf13/cobra"
	"go.uber.org/zap"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/ports"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/presentation/cli/tui"
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
		RunE: func(cmd *cobra.Command, _ []string) error {
			return tui.RunTui(cmd.Context(), deps.LoadConfig, cmd.InOrStdin(), cmd.OutOrStdout())
		},
		PersistentPreRunE: func(cmd *cobra.Command, _ []string) error {
			if envPath != "" {
				deps.ConfigRepo.SetPath(envPath)
			}
			return nil
		},
	}

	cmd.PersistentFlags().StringVar(&envPath, "env", ".env", "Path to the MineOS .env file")

	cmd.AddCommand(NewApiKeyCommand(deps.LoadConfig))
	cmd.AddCommand(NewConfigCommand(deps.LoadConfig))
	cmd.AddCommand(NewHealthCommand(deps.LoadConfig))
	cmd.AddCommand(NewInteractiveCommand(deps.LoadConfig))
	cmd.AddCommand(NewInstallCommand())
	cmd.AddCommand(NewLogsCommand(deps.LoadConfig))
	cmd.AddCommand(NewReconfigureCommand(deps.LoadConfig))
	cmd.AddCommand(NewStackCommand(deps.LoadConfig))
	cmd.AddCommand(NewTuiCommand(deps.LoadConfig))
	cmd.AddCommand(NewServersCommand(deps.LoadConfig))
	cmd.AddCommand(NewStatusCommand(deps.LoadConfig))
	cmd.AddCommand(NewUninstallCommand())
	cmd.AddCommand(NewVersionCommand(deps.Version))

	return cmd
}
