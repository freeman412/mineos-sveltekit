package usecases

import (
	"context"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/ports"
)

type StopAllServersUseCase struct {
	client ports.ApiClient
}

func NewStopAllServersUseCase(client ports.ApiClient) *StopAllServersUseCase {
	return &StopAllServersUseCase{client: client}
}

func (uc *StopAllServersUseCase) Execute(ctx context.Context, timeoutSeconds int) (ports.StopAllResult, error) {
	return uc.client.StopAll(ctx, timeoutSeconds)
}
