package commands

import (
	"context"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"strconv"
	"strings"
	"time"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

func NewStackCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	cmd := &cobra.Command{
		Use:   "stack",
		Short: "Manage MineOS Docker containers (not the CLI - use 'upgrade' for CLI updates)",
	}

	cmd.AddCommand(NewStackUpCommand(loadConfig))
	cmd.AddCommand(NewStackStopCommand(loadConfig))
	cmd.AddCommand(NewStackRestartCommand(loadConfig))
	cmd.AddCommand(NewStackDownCommand(loadConfig))
	cmd.AddCommand(NewStackPullCommand(loadConfig))
	cmd.AddCommand(NewStackBuildCommand(loadConfig))
	cmd.AddCommand(NewStackRecreateCommand(loadConfig))
	cmd.AddCommand(NewStackRebuildCommand(loadConfig))
	cmd.AddCommand(NewStackRebuildSourceCommand(loadConfig))
	cmd.AddCommand(NewStackUpdateCommand(loadConfig))
	cmd.AddCommand(NewStackUpdateSourceCommand(loadConfig))
	cmd.AddCommand(NewStackPsCommand(loadConfig))
	cmd.AddCommand(NewStackLogsCommand(loadConfig))

	return cmd
}

func NewStackUpCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var wait bool
	var waitTimeout int

	cmd := &cobra.Command{
		Use:     "up",
		Aliases: []string{"start"},
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

func NewStackStopCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
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

func NewStackRestartCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
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

func NewStackDownCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var timeout int
	var volumes bool

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

func NewStackPullCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var noBuild bool

	cmd := &cobra.Command{
		Use:   "pull",
		Short: "Pull Docker images",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := cmd.Context()

			var (
				compose composeRunner
				err     error
			)
			if noBuild {
				compose, _, err = loadComposeWithBuildOverride(ctx, loadConfig, false)
			} else {
				compose, _, err = loadComposeAndConfig(ctx, loadConfig)
			}
			if err != nil {
				return err
			}
			return compose.run([]string{"pull"})
		},
	}

	cmd.Flags().BoolVar(&noBuild, "no-build", false, "Ignore build-from-source config when pulling")

	return cmd
}

func NewStackBuildCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var force bool

	cmd := &cobra.Command{
		Use:   "build",
		Short: "Build Docker images from source",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := cmd.Context()
			out := cmd.OutOrStdout()

			var (
				compose composeRunner
				cfg     config.Config
				err     error
			)
			if force {
				compose, cfg, err = loadComposeWithBuildOverride(ctx, loadConfig, true)
			} else {
				compose, cfg, err = loadComposeAndConfig(ctx, loadConfig)
				if err == nil && !parseBool(cfg.BuildFromSource) {
					fmt.Fprintln(out, "Build-from-source is disabled; skipping image build.")
					return nil
				}
			}
			if err != nil {
				return err
			}

			if !dirExists("apps") {
				return errors.New("source files not found (./apps missing); build requires the repo source")
			}

			buildID := time.Now().Format("20060102150405")
			env := []string{"PUBLIC_BUILD_ID=" + buildID}
			args := []string{"build"}
			if compose.exe == "docker" {
				args = append(args, "--progress", "plain")
			}
			return compose.runWithEnv(args, env)
		},
	}

	cmd.Flags().BoolVar(&force, "force", false, "Force build regardless of config")

	return cmd
}

func NewStackRecreateCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var timeout int

	cmd := &cobra.Command{
		Use:   "recreate",
		Short: "Recreate containers from pulled images",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := cmd.Context()
			out := cmd.OutOrStdout()

			compose, cfg, err := loadComposeWithBuildOverride(ctx, loadConfig, false)
			if err != nil {
				return err
			}
			timeoutSeconds := effectiveShutdownTimeout(cfg, timeout)
			if err := gracefulStop(ctx, loadConfig, compose, cfg, timeoutSeconds, false, out); err != nil {
				return err
			}
			if err := compose.down(false); err != nil {
				return err
			}
			if err := compose.run([]string{"pull"}); err != nil {
				return err
			}
			return compose.run([]string{"up", "-d", "--force-recreate"})
		},
	}

	cmd.Flags().IntVar(&timeout, "timeout", 0, "Shutdown timeout in seconds (default from .env)")

	return cmd
}

func NewStackRebuildCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var timeout int

	cmd := &cobra.Command{
		Use:   "rebuild",
		Short: "Rebuild containers (respect build-from-source setting)",
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
			if err := compose.down(false); err != nil {
				return err
			}

			if parseBool(cfg.BuildFromSource) {
				if err := buildWithCompose(compose); err != nil {
					return err
				}
			} else {
				if err := compose.run([]string{"pull"}); err != nil {
					return err
				}
			}
			return compose.run([]string{"up", "-d", "--force-recreate"})
		},
	}

	cmd.Flags().IntVar(&timeout, "timeout", 0, "Shutdown timeout in seconds (default from .env)")

	return cmd
}

func NewStackRebuildSourceCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var timeout int

	cmd := &cobra.Command{
		Use:     "rebuild-source",
		Aliases: []string{"rebuild-from-source"},
		Short:   "Rebuild containers from source",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := cmd.Context()
			out := cmd.OutOrStdout()

			compose, cfg, err := loadComposeWithBuildOverride(ctx, loadConfig, true)
			if err != nil {
				return err
			}
			timeoutSeconds := effectiveShutdownTimeout(cfg, timeout)
			if err := gracefulStop(ctx, loadConfig, compose, cfg, timeoutSeconds, false, out); err != nil {
				return err
			}
			if err := compose.down(false); err != nil {
				return err
			}

			if err := buildWithCompose(compose); err != nil {
				return err
			}
			return compose.run([]string{"up", "-d", "--force-recreate"})
		},
	}

	cmd.Flags().IntVar(&timeout, "timeout", 0, "Shutdown timeout in seconds (default from .env)")

	return cmd
}

func NewStackUpdateCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var timeout int

	cmd := &cobra.Command{
		Use:   "update",
		Short: "Pull latest Docker images and recreate containers (does NOT update CLI)",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := cmd.Context()
			out := cmd.OutOrStdout()

			// Force non-build mode for update - always pull images
			compose, cfg, err := loadComposeWithBuildOverride(ctx, loadConfig, false)
			if err != nil {
				return err
			}

			tag := strings.TrimSpace(cfg.ImageTag)
			channel := "stable (latest)"
			if tag == "preview" {
				channel = "preview"
			} else if tag != "" && tag != "latest" {
				channel = "pinned (" + tag + ")"
			}
			fmt.Fprintf(out, "Pulling images (%s)...\n", channel)
			if err := compose.run([]string{"pull"}); err != nil {
				return err
			}

			timeoutSeconds := effectiveShutdownTimeout(cfg, timeout)
			if err := gracefulStop(ctx, loadConfig, compose, cfg, timeoutSeconds, false, out); err != nil {
				return err
			}

			fmt.Fprintln(out, "Recreating containers with new images...")
			return compose.run([]string{"up", "-d", "--force-recreate"})
		},
	}

	cmd.Flags().IntVar(&timeout, "timeout", 0, "Shutdown timeout in seconds (default from .env)")

	return cmd
}

func NewStackUpdateSourceCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:   "update-source",
		Short: "Pull latest source and rebuild from source",
		RunE: func(cmd *cobra.Command, _ []string) error {
			if err := runGitPull(cmd.OutOrStdout()); err != nil {
				return err
			}
			return NewStackRebuildSourceCommand(loadConfig).RunE(cmd, []string{})
		},
	}
}

func NewStackPsCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:   "ps",
		Short: "Show Docker compose status",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := cmd.Context()
			compose, _, err := loadComposeAndConfig(ctx, loadConfig)
			if err != nil {
				return err
			}
			return compose.run([]string{"ps"})
		},
	}
}

func NewStackLogsCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
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

func gracefulStop(ctx context.Context, loadConfig *usecases.LoadConfigUseCase, compose composeRunner, cfg config.Config, timeoutSeconds int, force bool, out io.Writer) error {
	if force {
		fmt.Fprintln(out, "Force stop enabled; killing servers and stopping containers immediately.")
		if err := stopMinecraftServers(ctx, loadConfig, out, true, timeoutSeconds); err != nil {
			fmt.Fprintf(out, "Warning: %v\n", err)
		}
		if err := compose.run([]string{"stop", "-t", "0"}); err != nil {
			return err
		}
		return nil
	}

	if err := stopMinecraftServers(ctx, loadConfig, out, false, timeoutSeconds); err != nil {
		fmt.Fprintf(out, "Warning: %v\n", err)
	}

	// Minecraft servers are already stopped via the API above.
	// Docker containers (API, web, caddy) should exit promptly on SIGTERM,
	// so use a short timeout for docker compose stop.
	const dockerStopTimeout = 30
	fmt.Fprintln(out, "Stopping Docker services...")
	if err := compose.run([]string{"stop", "-t", strconv.Itoa(dockerStopTimeout)}); err != nil {
		return err
	}
	return nil
}

func stopMinecraftServers(ctx context.Context, loadConfig *usecases.LoadConfigUseCase, out io.Writer, force bool, timeoutSeconds int) error {
	_, err := withApiKeyRetry(ctx, loadConfig, out, func(_ config.Config, client *api.Client) error {
		// Check if there are any servers first to avoid waiting on empty stop-all
		servers, err := client.ListServers(ctx)
		if err != nil {
			return err
		}
		if len(servers) == 0 {
			fmt.Fprintln(out, "No servers found.")
			return nil
		}

		if force {
			for _, server := range servers {
				fmt.Fprintf(out, "Killing server: %s\n", server.Name)
				if err := client.ServerAction(ctx, server.Name, "kill"); err != nil {
					return err
				}
			}
			return nil
		}

		result, err := client.StopAll(ctx, timeoutSeconds)
		if err != nil {
			return err
		}
		fmt.Fprintf(out, "Stop-all requested. Total: %d, running: %d, stopped: %d, skipped: %d\n", result.Total, result.Running, result.Stopped, result.Skipped)
		for _, item := range result.Results {
			if item.Error != "" {
				fmt.Fprintf(out, "%s\t%s\t%s\n", item.Name, item.Status, item.Error)
			} else {
				fmt.Fprintf(out, "%s\t%s\n", item.Name, item.Status)
			}
		}
		return nil
	})
	return err
}

func buildWithCompose(compose composeRunner) error {
	if !dirExists("apps") {
		return errors.New("source files not found (./apps missing); build requires the repo source")
	}
	buildID := time.Now().Format("20060102150405")
	env := []string{"PUBLIC_BUILD_ID=" + buildID}
	args := []string{"build"}
	if compose.exe == "docker" {
		args = append(args, "--progress", "plain")
	}
	return compose.runWithEnv(args, env)
}

func runGitPull(out io.Writer) error {
	if _, err := exec.LookPath("git"); err != nil {
		return errors.New("git is not installed")
	}
	fmt.Fprintln(out, "Pulling latest changes...")
	cmd := exec.Command("git", "pull")
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr
	return cmd.Run()
}
