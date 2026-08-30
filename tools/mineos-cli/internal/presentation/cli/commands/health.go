package commands

import (
	"context"
	"fmt"
	"sort"
	"time"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

func NewHealthCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var all bool
	cmd := &cobra.Command{
		Use:   "health",
		Short: "Check MineOS API health",
		Long:  "Check MineOS API health. With --all, also show the watchdog roll-up, recent crashes, and active alerts.",
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
			if !all {
				return nil
			}
			return printHealthRollup(ctx, cmd, client)
		},
	}
	cmd.Flags().BoolVar(&all, "all", false, "include watchdog status, recent crashes, and active alerts")
	return cmd
}

func printHealthRollup(ctx context.Context, cmd *cobra.Command, client *api.Client) error {
	watchdog, err := client.WatchdogStatus(ctx)
	if err != nil {
		return fmt.Errorf("watchdog status: %w", err)
	}
	crashes, err := client.WatchdogCrashes(ctx, 10)
	if err != nil {
		return fmt.Errorf("crash history: %w", err)
	}
	alerts, err := client.ActiveNotifications(ctx)
	if err != nil {
		return fmt.Errorf("notifications: %w", err)
	}

	now := time.Now()

	cmd.Println("\nWatchdog:")
	if len(watchdog) == 0 {
		cmd.Println("  (no servers monitored)")
	}
	names := make([]string, 0, len(watchdog))
	for name := range watchdog {
		names = append(names, name)
	}
	sort.Strings(names)
	for _, name := range names {
		s := watchdog[name]
		state := "idle"
		if s.CooldownEndsAt != nil && s.CooldownEndsAt.After(now) {
			state = "cooldown until " + s.CooldownEndsAt.Local().Format("15:04:05")
		} else if s.IsMonitoring {
			state = "monitoring"
		}
		line := fmt.Sprintf("  %-20s %s", name, state)
		if s.RestartAttempts > 0 {
			line += fmt.Sprintf("  restarts=%d", s.RestartAttempts)
		}
		if s.LastCrashTime != nil {
			line += "  last-crash=" + s.LastCrashTime.Local().Format("2006-01-02 15:04")
		}
		cmd.Println(line)
	}

	cmd.Println("\nRecent crashes:")
	if len(crashes) == 0 {
		cmd.Println("  (none)")
	}
	for _, c := range crashes {
		restart := "no auto-restart"
		if c.AutoRestartAttempted {
			if c.AutoRestartSucceeded {
				restart = "auto-restart ok"
			} else {
				restart = "auto-restart FAILED"
			}
		}
		cmd.Printf("  %s  %-20s %-13s %s\n",
			c.DetectedAt.Local().Format("2006-01-02 15:04"), c.ServerName, c.CrashType, restart)
	}

	unread := 0
	for _, a := range alerts {
		if !a.IsRead {
			unread++
		}
	}
	cmd.Printf("\nAlerts (%d active, %d unread):\n", len(alerts), unread)
	if len(alerts) == 0 {
		cmd.Println("  (none)")
	}
	for _, a := range alerts {
		scope := ""
		if a.ServerName != nil && *a.ServerName != "" {
			scope = " [" + *a.ServerName + "]"
		}
		marker := " "
		if !a.IsRead {
			marker = "*"
		}
		cmd.Printf("  %s %-8s %s%s — %s\n", marker, a.Type, a.Title, scope, a.Message)
	}
	return nil
}
