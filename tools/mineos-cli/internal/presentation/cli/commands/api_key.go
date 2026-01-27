package commands

import (
	"fmt"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
)

func NewApiKeyCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	cmd := &cobra.Command{
		Use:   "api-key",
		Short: "Manage MineOS API keys",
	}

	cmd.AddCommand(NewApiKeyRefreshCommand(loadConfig))
	return cmd
}

func NewApiKeyRefreshCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:   "refresh",
		Short: "Refresh MINEOS_API_KEY from the local sqlite database",
		RunE: func(cmd *cobra.Command, _ []string) error {
			cfg, err := loadConfig.Execute(cmd.Context())
			if err != nil {
				return err
			}
			key, err := refreshApiKeyFromDb(cfg)
			if err != nil {
				return err
			}
			fmt.Fprintln(cmd.OutOrStdout(), "API key refreshed from local database.")
			fmt.Fprintf(cmd.OutOrStdout(), "MINEOS_API_KEY: %s\n", mask(key))
			return nil
		},
	}
}
