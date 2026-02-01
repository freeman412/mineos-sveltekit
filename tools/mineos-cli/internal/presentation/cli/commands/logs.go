package commands

import (
	"context"
	"fmt"
	"os"
	"os/signal"
	"time"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

func NewServerLogsCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var source string

	cmd := &cobra.Command{
		Use:   "logs <server>",
		Short: "Stream Minecraft server logs",
		Long: `Stream logs from a Minecraft server.

Available log sources:
  combined  - All logs combined (default)
  server    - Server console output
  java      - Java/JVM output
  crash     - Crash reports`,
		Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			serverName := args[0]
			out := cmd.OutOrStdout()

			ctx := context.Background()
			_, err := withApiKeyRetry(ctx, loadConfig, out, func(_ config.Config, client *api.Client) error {
				// Verify server exists
				_, err := client.ListServers(ctx)
				return err
			})
			if err != nil {
				return err
			}

			cfg, err := loadConfig.Execute(ctx)
			if err != nil {
				return err
			}
			client := api.NewClientFromConfig(cfg)

			fmt.Fprintf(out, "Streaming logs for %s (%s). Press Ctrl+C to stop.\n", serverName, source)

			streamCtx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
			defer stop()

			logs, errs := client.StreamConsoleLogs(streamCtx, serverName, source)
			for {
				select {
				case entry, ok := <-logs:
					if !ok {
						return nil
					}
					if entry.Timestamp.IsZero() {
						fmt.Fprintln(out, entry.Message)
					} else {
						fmt.Fprintf(out, "[%s] %s\n", entry.Timestamp.Format(time.RFC3339), entry.Message)
					}
				case err, ok := <-errs:
					if ok && err != nil {
						return err
					}
				case <-streamCtx.Done():
					fmt.Fprintln(out, "\nLog stream stopped.")
					return nil
				}
			}
		},
	}

	cmd.Flags().StringVarP(&source, "source", "s", "combined", "Log source (combined, server, java, crash)")

	return cmd
}
