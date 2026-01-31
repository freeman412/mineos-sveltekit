package commands

import (
	"context"
	"fmt"
	"io"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
)

const (
	defaultShutdownTimeout = 300
)

func loadComposeAndConfig(ctx context.Context, loadConfig *usecases.LoadConfigUseCase) (composeRunner, config.Config, error) {
	compose, err := detectCompose()
	if err != nil {
		return composeRunner{}, config.Config{}, err
	}
	cfg, err := loadConfig.Execute(ctx)
	if err != nil {
		return composeRunner{}, config.Config{}, err
	}
	return composeWithConfig(compose, cfg), cfg, nil
}

func loadComposeWithBuildOverride(ctx context.Context, loadConfig *usecases.LoadConfigUseCase, buildFromSource bool) (composeRunner, config.Config, error) {
	compose, err := detectCompose()
	if err != nil {
		return composeRunner{}, config.Config{}, err
	}
	cfg, err := loadConfig.Execute(ctx)
	if err != nil {
		return composeRunner{}, config.Config{}, err
	}
	cfg.BuildFromSource = strconv.FormatBool(buildFromSource)
	return composeWithConfig(compose, cfg), cfg, nil
}

func effectiveShutdownTimeout(cfg config.Config, override int) int {
	if override > 0 {
		return override
	}
	if value := strings.TrimSpace(cfg.ShutdownTimeout); value != "" {
		if parsed, err := strconv.Atoi(value); err == nil && parsed > 0 {
			return parsed
		}
	}
	return defaultShutdownTimeout
}

func waitForApiReady(ctx context.Context, cfg config.Config, out io.Writer, timeoutSeconds int) error {
	if timeoutSeconds <= 0 {
		timeoutSeconds = 60
	}
	apiPort := strings.TrimSpace(cfg.ApiPort)
	if apiPort == "" {
		apiPort = strconv.Itoa(defaultApiPort)
	}

	url := fmt.Sprintf("http://localhost:%s/health", apiPort)
	client := &http.Client{Timeout: 5 * time.Second}
	deadline := time.Now().Add(time.Duration(timeoutSeconds) * time.Second)

	if out != nil {
		fmt.Fprintln(out, "Waiting for API to be ready...")
	}

	for {
		req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
		if err == nil {
			resp, err := client.Do(req)
			if err == nil {
				_ = resp.Body.Close()
				if resp.StatusCode >= 200 && resp.StatusCode < 300 {
					if out != nil {
						fmt.Fprintln(out, "API is ready.")
					}
					return nil
				}
			}
		}

		if time.Now().After(deadline) {
			return fmt.Errorf("API did not become ready within %ds", timeoutSeconds)
		}

		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-time.After(2 * time.Second):
		}
	}
}

