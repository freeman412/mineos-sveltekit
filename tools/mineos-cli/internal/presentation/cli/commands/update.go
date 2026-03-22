package commands

import (
	"fmt"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
)

func NewUpdateCommand(loadConfig *usecases.LoadConfigUseCase, currentVersion string) *cobra.Command {
	var timeout int
	var skipCli bool
	var skipStack bool
	var force bool
	var prerelease bool

	cmd := &cobra.Command{
		Use:   "update",
		Short: "Update everything — CLI binary and Docker containers",
		Long: `Update MineOS completely: upgrade the CLI binary and pull + recreate Docker containers.

This is the recommended way to update MineOS. It combines:
  1. CLI upgrade   (same as 'mineos upgrade')
  2. Stack update  (same as 'mineos stack update')

Use --skip-cli or --skip-stack to update only one component.

Examples:
  mineos update              # Update CLI + containers
  mineos update --skip-cli   # Only update containers (pull + recreate)
  mineos update --skip-stack # Only update CLI binary
  mineos update --prerelease # Include pre-release versions`,
		RunE: func(cmd *cobra.Command, _ []string) error {
			out := cmd.OutOrStdout()

			if skipCli && skipStack {
				return fmt.Errorf("cannot skip both CLI and stack — nothing to update")
			}

			// Step 1: CLI upgrade
			if !skipCli {
				fmt.Fprintln(out, "")
				fmt.Fprintln(out, "━━━ Step 1: Updating CLI binary ━━━")
				fmt.Fprintln(out, "")
				if err := runUpgrade(cmd, currentVersion, force, false, prerelease); err != nil {
					fmt.Fprintf(out, "CLI upgrade failed: %v\n", err)
					fmt.Fprintln(out, "Continuing with stack update...")
				}
			}

			// Step 2: Stack update
			if !skipStack {
				fmt.Fprintln(out, "")
				fmt.Fprintln(out, "━━━ Step 2: Updating Docker containers ━━━")
				fmt.Fprintln(out, "")
				stackCmd := NewStackUpdateCommand(loadConfig)
				if timeout > 0 {
					_ = stackCmd.Flags().Set("timeout", fmt.Sprintf("%d", timeout))
				}
				if err := stackCmd.RunE(cmd, []string{}); err != nil {
					return fmt.Errorf("stack update failed: %w", err)
				}
			}

			fmt.Fprintln(out, "")
			fmt.Fprintln(out, "✓ MineOS update complete!")
			return nil
		},
	}

	cmd.Flags().IntVar(&timeout, "timeout", 0, "Shutdown timeout in seconds (default from .env)")
	cmd.Flags().BoolVar(&skipCli, "skip-cli", false, "Skip CLI binary upgrade, only update containers")
	cmd.Flags().BoolVar(&skipStack, "skip-stack", false, "Skip container update, only upgrade CLI binary")
	cmd.Flags().BoolVar(&force, "force", false, "Force CLI upgrade even if already on latest")
	cmd.Flags().BoolVar(&prerelease, "prerelease", false, "Include pre-release/beta versions")

	return cmd
}
