package commands

import (
	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/presentation/cli/tui"
)

func NewTuiCommand(loadConfig *usecases.LoadConfigUseCase, version string) *cobra.Command {
	return &cobra.Command{
		Use:     "tui",
		Aliases: []string{"ui"},
		Short:   "Full-screen MineOS dashboard",
		RunE: func(cmd *cobra.Command, _ []string) error {
			// This command is kept for explicit access, but the default `mineos` already launches the TUI.
			return tui.RunTui(cmd.Context(), loadConfig, version, cmd.InOrStdin(), cmd.OutOrStdout())
		},
	}
}
