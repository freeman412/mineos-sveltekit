package usecases

import (
	"context"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/ports"
)

type HealthCheckUseCase struct {
	client ports.ApiClient
}

func NewHealthCheckUseCase(client ports.ApiClient) *HealthCheckUseCase {
	return &HealthCheckUseCase{client: client}
}

func (uc *HealthCheckUseCase) Execute(ctx context.Context) error {
	return uc.client.Health(ctx)
}
