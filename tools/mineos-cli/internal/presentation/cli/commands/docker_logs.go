package commands

import (
	"strconv"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
)

func NewDockerLogsCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var tail int
	var follow bool

	cmd := &cobra.Command{
		Use:   "logs [service]",
		Short: "Stream Docker compose logs",
		Args:  cobra.MaximumNArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			compose, _, err := loadComposeAndConfig(cmd.Context(), loadConfig)
			if err != nil {
				return err
			}

			composeArgs := []string{"logs"}
			if follow {
				composeArgs = append(composeArgs, "-f")
			}
			if tail > 0 {
				composeArgs = append(composeArgs, "--tail", strconv.Itoa(tail))
			}
			if len(args) == 1 {
				composeArgs = append(composeArgs, args[0])
			}
			return compose.run(composeArgs)
		},
	}

	cmd.Flags().IntVar(&tail, "tail", 200, "Number of log lines to show")
	cmd.Flags().BoolVar(&follow, "follow", true, "Follow log output")

	return cmd
}
