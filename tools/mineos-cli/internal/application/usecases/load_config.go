package usecases

import (
	"context"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/ports"
)

type LoadConfigUseCase struct {
	repo ports.ConfigRepository
}

func NewLoadConfigUseCase(repo ports.ConfigRepository) *LoadConfigUseCase {
	return &LoadConfigUseCase{repo: repo}
}

func (uc *LoadConfigUseCase) Execute(ctx context.Context) (config.Config, error) {
	return uc.repo.Load(ctx)
}
