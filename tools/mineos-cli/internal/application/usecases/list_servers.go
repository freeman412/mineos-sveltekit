package usecases

import (
	"context"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/ports"
)

type ListServersUseCase struct {
	client ports.ApiClient
}

func NewListServersUseCase(client ports.ApiClient) *ListServersUseCase {
	return &ListServersUseCase{client: client}
}

func (uc *ListServersUseCase) Execute(ctx context.Context) ([]ports.Server, error) {
	return uc.client.ListServers(ctx)
}
