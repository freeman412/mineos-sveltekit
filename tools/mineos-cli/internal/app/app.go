package app

import (
	"github.com/spf13/cobra"
	"go.uber.org/zap"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/env"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/presentation/cli/commands"
)

var Version = "dev"

type App struct {
	rootCmd *cobra.Command
	logger  *zap.Logger
}

func New() (*App, error) {
	logger, _ := zap.NewProduction()
	configRepo := env.NewDotenvRepository(".env")
	loadConfig := usecases.NewLoadConfigUseCase(configRepo)

	rootCmd := commands.NewRootCommand(commands.RootDeps{
		ConfigRepo: configRepo,
		LoadConfig: loadConfig,
		Logger:     logger,
		Version:    Version,
	})

	return &App{rootCmd: rootCmd, logger: logger}, nil
}

func (a *App) Run() error {
	defer func() {
		_ = a.logger.Sync()
	}()
	return a.rootCmd.Execute()
}
