package commands

import (
	"fmt"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
)

func NewStartCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var wait bool
	var waitTimeout int

	cmd := &cobra.Command{
		Use:     "start",
		Aliases: []string{"up"},
		Short:   "Start MineOS Docker containers",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := cmd.Context()
			out := cmd.OutOrStdout()

			compose, cfg, err := loadComposeAndConfig(ctx, loadConfig)
			if err != nil {
				return err
			}
			if err := compose.run([]string{"up", "-d"}); err != nil {
				return err
			}

			if wait {
				if err := waitForApiReady(ctx, cfg, out, waitTimeout); err != nil {
					fmt.Fprintf(out, "Warning: %v\n", err)
				}
			}
			return nil
		},
	}

	cmd.Flags().BoolVar(&wait, "wait", true, "Wait for API health after startup")
	cmd.Flags().IntVar(&waitTimeout, "wait-timeout", 60, "Seconds to wait for API health")

	return cmd
}

func NewStopCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var force bool
	var timeout int

	cmd := &cobra.Command{
		Use:   "stop",
		Short: "Stop MineOS Docker containers",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := cmd.Context()
			out := cmd.OutOrStdout()

			compose, cfg, err := loadComposeAndConfig(ctx, loadConfig)
			if err != nil {
				return err
			}
			timeoutSeconds := effectiveShutdownTimeout(cfg, timeout)
			return gracefulStop(ctx, loadConfig, compose, cfg, timeoutSeconds, force, out)
		},
	}

	cmd.Flags().BoolVar(&force, "force", false, "Force stop servers immediately")
	cmd.Flags().IntVar(&timeout, "timeout", 0, "Shutdown timeout in seconds (default from .env)")

	return cmd
}

func NewRestartCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var wait bool
	var waitTimeout int
	var timeout int

	cmd := &cobra.Command{
		Use:   "restart",
		Short: "Restart MineOS Docker containers",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := cmd.Context()
			out := cmd.OutOrStdout()

			compose, cfg, err := loadComposeAndConfig(ctx, loadConfig)
			if err != nil {
				return err
			}
			timeoutSeconds := effectiveShutdownTimeout(cfg, timeout)
			if err := gracefulStop(ctx, loadConfig, compose, cfg, timeoutSeconds, false, out); err != nil {
				return err
			}
			if err := compose.run([]string{"up", "-d"}); err != nil {
				return err
			}
			if wait {
				if err := waitForApiReady(ctx, cfg, out, waitTimeout); err != nil {
					fmt.Fprintf(out, "Warning: %v\n", err)
				}
			}
			return nil
		},
	}

	cmd.Flags().BoolVar(&wait, "wait", true, "Wait for API health after restart")
	cmd.Flags().IntVar(&waitTimeout, "wait-timeout", 60, "Seconds to wait for API health")
	cmd.Flags().IntVar(&timeout, "timeout", 0, "Shutdown timeout in seconds (default from .env)")

	return cmd
}

func NewPsCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:   "ps",
		Short: "Show Docker compose container status",
		RunE: func(cmd *cobra.Command, _ []string) error {
			compose, _, err := loadComposeAndConfig(cmd.Context(), loadConfig)
			if err != nil {
				return err
			}
			return compose.run([]string{"ps"})
		},
	}
}

func NewPullCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:   "pull",
		Short: "Pull the latest Docker images",
		RunE: func(cmd *cobra.Command, _ []string) error {
			compose, _, err := loadComposeAndConfig(cmd.Context(), loadConfig)
			if err != nil {
				return err
			}
			return compose.run([]string{"pull"})
		},
	}
}

func NewDownCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var volumes bool
	var timeout int

	cmd := &cobra.Command{
		Use:   "down",
		Short: "Stop and remove MineOS containers",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := cmd.Context()
			out := cmd.OutOrStdout()

			compose, cfg, err := loadComposeAndConfig(ctx, loadConfig)
			if err != nil {
				return err
			}
			timeoutSeconds := effectiveShutdownTimeout(cfg, timeout)
			if err := gracefulStop(ctx, loadConfig, compose, cfg, timeoutSeconds, false, out); err != nil {
				return err
			}
			return compose.down(volumes)
		},
	}

	cmd.Flags().BoolVar(&volumes, "volumes", false, "Also remove Docker volumes")
	cmd.Flags().IntVar(&timeout, "timeout", 0, "Shutdown timeout in seconds (default from .env)")

	return cmd
}
