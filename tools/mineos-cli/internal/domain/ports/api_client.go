package ports

import "context"

type Server struct {
	Name   string `json:"name"`
	Status string `json:"status"`
}

type StopAllResult struct {
	Total   int            `json:"total"`
	Running int            `json:"running"`
	Stopped int            `json:"stopped"`
	Skipped int            `json:"skipped"`
	Results []StopAllItem  `json:"results"`
}

type StopAllItem struct {
	Name   string `json:"name"`
	Status string `json:"status"`
	Error  string `json:"error"`
}

type ApiClient interface {
	Health(ctx context.Context) error
	ListServers(ctx context.Context) ([]Server, error)
	StopAll(ctx context.Context, timeoutSeconds int) (StopAllResult, error)
	ServerAction(ctx context.Context, name, action string) error
}
