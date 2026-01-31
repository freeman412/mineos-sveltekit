package commands

import (
	"fmt"
	"os"

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

			// Skip .env check for commands that don't need it
			skipEnvCheck := cmd.Name() == "install" || cmd.Name() == "upgrade" || cmd.Name() == "version" || cmd.Name() == "help"
			if skipEnvCheck {
				return nil
			}

			// Check if .env file exists
			effectivePath := envPath
			if effectivePath == "" {
				effectivePath = ".env"
			}
			if _, err := os.Stat(effectivePath); os.IsNotExist(err) {
				pwd, _ := os.Getwd()
				msg := fmt.Sprintf("\n.env file not found at: %s\n\n", effectivePath)
				msg += "MineOS is not installed in this directory.\n\n"
				msg += "To install MineOS, run:\n"
				msg += "  mineos install\n\n"
				msg += "If MineOS is installed elsewhere:\n"
				msg += "  1. Navigate to the installation directory, OR\n"
				msg += fmt.Sprintf("  2. Use --env flag: mineos --env /path/to/.env %s\n\n", cmd.Name())
				msg += fmt.Sprintf("Current directory: %s\n", pwd)
				return fmt.Errorf(msg)
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
	cmd.AddCommand(NewUpgradeCommand(deps.Version))
	cmd.AddCommand(NewVersionCommand(deps.Version))

	return cmd
}
