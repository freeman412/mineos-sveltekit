package ports

import (
	"context"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
)

type ConfigRepository interface {
	Load(ctx context.Context) (config.Config, error)
	SetPath(path string)
	Path() string
}
