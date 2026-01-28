package usecases

import (
	"context"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/ports"
)

type ServerActionUseCase struct {
	client ports.ApiClient
}

func NewServerActionUseCase(client ports.ApiClient) *ServerActionUseCase {
	return &ServerActionUseCase{client: client}
}

func (uc *ServerActionUseCase) Execute(ctx context.Context, name, action string) error {
	return uc.client.ServerAction(ctx, name, action)
}
