package app

import (
	"fmt"
	"os"
	"time"

	"github.com/spf13/cobra"
	"go.uber.org/zap"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/env"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/presentation/cli/commands"
)

var Version = "dev"

type App struct {
	rootCmd      *cobra.Command
	logger       *zap.Logger
	updateNotice chan string
}

func New() (*App, error) {
	logger, err := zap.NewProduction()
	if err != nil {
		// Fallback to no-op logger if production logger fails
		logger = zap.NewNop()
	}
	configRepo := env.NewDotenvRepository(".env")
	loadConfig := usecases.NewLoadConfigUseCase(configRepo)

	rootCmd := commands.NewRootCommand(commands.RootDeps{
		ConfigRepo: configRepo,
		LoadConfig: loadConfig,
		Logger:     logger,
		Version:    Version,
	})

	app := &App{
		rootCmd:      rootCmd,
		logger:       logger,
		updateNotice: make(chan string, 1),
	}

	// Start background version check (non-blocking)
	go app.checkForUpdates()

	return app, nil
}

func (a *App) Run() error {
	defer func() {
		_ = a.logger.Sync()
		// Print update notice if available (after command completes)
		a.printUpdateNotice()
	}()
	return a.rootCmd.Execute()
}

func (a *App) checkForUpdates() {
	// Only check for non-dev versions
	if Version == "dev" || Version == "" {
		return
	}

	notice := commands.CheckForUpdates(Version)
	if notice != "" {
		a.updateNotice <- notice
	}
	close(a.updateNotice)
}

func (a *App) printUpdateNotice() {
	// Wait briefly for the version check to complete
	select {
	case notice := <-a.updateNotice:
		if notice != "" {
			fmt.Fprintln(os.Stderr, "")
			fmt.Fprintln(os.Stderr, notice)
		}
	case <-time.After(100 * time.Millisecond):
		// Don't wait too long, just skip if check is slow
	}
}
