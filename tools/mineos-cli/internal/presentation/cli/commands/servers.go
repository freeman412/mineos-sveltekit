package commands

import (
	"context"
	"fmt"
	"sort"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

func NewServersCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	cmd := &cobra.Command{
		Use:   "servers",
		Short: "Server management commands",
	}

	cmd.AddCommand(NewServersListCommand(loadConfig))
	cmd.AddCommand(NewServersStopAllCommand(loadConfig))
	cmd.AddCommand(NewServerActionCommand(loadConfig, "start"))
	cmd.AddCommand(NewServerActionCommand(loadConfig, "stop"))
	cmd.AddCommand(NewServerActionCommand(loadConfig, "restart"))
	cmd.AddCommand(NewServerActionCommand(loadConfig, "kill"))

	return cmd
}

func NewServersListCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:   "list",
		Short: "List servers",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := context.Background()
			cfg, err := loadConfig.Execute(ctx)
			if err != nil {
				return err
			}
			client := api.NewClientFromConfig(cfg)
			uc := usecases.NewListServersUseCase(client)
			servers, err := uc.Execute(ctx)
			if err != nil {
				return err
			}
			if len(servers) == 0 {
				cmd.Println("No servers found.")
				return nil
			}
			sort.Slice(servers, func(i, j int) bool { return servers[i].Name < servers[j].Name })
			for _, server := range servers {
				cmd.Printf("%s\t%s\n", server.Name, server.Status)
			}
			return nil
		},
	}
}

func NewServersStopAllCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	var timeout int

	cmd := &cobra.Command{
		Use:   "stop-all",
		Short: "Stop all running servers",
		RunE: func(cmd *cobra.Command, _ []string) error {
			ctx := context.Background()
			cfg, err := loadConfig.Execute(ctx)
			if err != nil {
				return err
			}
			client := api.NewClientFromConfig(cfg)
			uc := usecases.NewStopAllServersUseCase(client)
			result, err := uc.Execute(ctx, timeout)
			if err != nil {
				return err
			}
			cmd.Printf("Total: %d, running: %d, stopped: %d, skipped: %d\n", result.Total, result.Running, result.Stopped, result.Skipped)
			for _, item := range result.Results {
				if item.Error != "" {
					cmd.Printf("%s\t%s\t%s\n", item.Name, item.Status, item.Error)
				} else {
					cmd.Printf("%s\t%s\n", item.Name, item.Status)
				}
			}
			return nil
		},
	}

	cmd.Flags().IntVar(&timeout, "timeout", 300, "Shutdown timeout in seconds")

	return cmd
}

func NewServerActionCommand(loadConfig *usecases.LoadConfigUseCase, action string) *cobra.Command {
	return &cobra.Command{
		Use:   fmt.Sprintf("%s <name>", action),
		Short: fmt.Sprintf("%s a server", action),
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			ctx := context.Background()
			cfg, err := loadConfig.Execute(ctx)
			if err != nil {
				return err
			}
			client := api.NewClientFromConfig(cfg)
			uc := usecases.NewServerActionUseCase(client)
			if err := uc.Execute(ctx, args[0], action); err != nil {
				return err
			}
			cmd.Printf("%s: %s\n", action, args[0])
			return nil
		},
	}
}
