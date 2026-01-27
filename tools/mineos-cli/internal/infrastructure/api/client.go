package api

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
	"time"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/ports"
)

type Client struct {
	baseURL    string
	apiBaseURL string
	apiKey     string
	httpClient *http.Client
}

func NewClient(baseURL, apiKey string) *Client {
	base := strings.TrimRight(baseURL, "/")
	return &Client{
		baseURL:    base,
		apiBaseURL: base + "/api/v1",
		apiKey:     strings.TrimSpace(apiKey),
		httpClient: &http.Client{Timeout: 15 * time.Second},
	}
}

func NewClientFromConfig(cfg config.Config) *Client {
	apiPort := cfg.ApiPort
	if apiPort == "" {
		apiPort = "5078"
	}
	base := "http://localhost:" + apiPort
	return NewClient(base, cfg.EffectiveApiKey())
}

func (c *Client) Health(ctx context.Context) error {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, c.baseURL+"/health", nil)
	if err != nil {
		return err
	}
	resp, err := c.httpClient.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode >= 200 && resp.StatusCode < 300 {
		return nil
	}
	return fmt.Errorf("health check failed: %s", readBody(resp.Body))
}

func (c *Client) ListServers(ctx context.Context) ([]ports.Server, error) {
	if strings.TrimSpace(c.apiKey) == "" {
		return nil, errors.New("API key missing; set MINEOS_API_KEY in .env or provide ApiKey__StaticKey")
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, c.apiBaseURL+"/servers/list", nil)
	if err != nil {
		return nil, err
	}
	req.Header.Set("X-Api-Key", c.apiKey)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusForbidden || resp.StatusCode == http.StatusUnauthorized {
		return nil, errors.New("invalid API key")
	}

	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return nil, fmt.Errorf("list servers failed: %s", readBody(resp.Body))
	}

	var servers []ports.Server
	if err := json.NewDecoder(resp.Body).Decode(&servers); err != nil {
		return nil, err
	}

	return servers, nil
}

func (c *Client) StopAll(ctx context.Context, timeoutSeconds int) (ports.StopAllResult, error) {
	if strings.TrimSpace(c.apiKey) == "" {
		return ports.StopAllResult{}, errors.New("API key missing; set MINEOS_API_KEY in .env or provide ApiKey__StaticKey")
	}
	url := fmt.Sprintf("%s/servers/actions/stop-all?timeoutSeconds=%d", c.apiBaseURL, timeoutSeconds)
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, nil)
	if err != nil {
		return ports.StopAllResult{}, err
	}
	req.Header.Set("X-Api-Key", c.apiKey)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return ports.StopAllResult{}, err
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusForbidden || resp.StatusCode == http.StatusUnauthorized {
		return ports.StopAllResult{}, errors.New("invalid API key")
	}
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return ports.StopAllResult{}, fmt.Errorf("stop-all failed: %s", readBody(resp.Body))
	}

	var result ports.StopAllResult
	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return ports.StopAllResult{}, err
	}
	return result, nil
}

func (c *Client) ServerAction(ctx context.Context, name, action string) error {
	if strings.TrimSpace(c.apiKey) == "" {
		return errors.New("API key missing; set MINEOS_API_KEY in .env or provide ApiKey__StaticKey")
	}
	if strings.TrimSpace(name) == "" {
		return errors.New("server name is required")
	}
	if strings.TrimSpace(action) == "" {
		return errors.New("action is required")
	}

	url := fmt.Sprintf("%s/servers/%s/actions/%s", c.apiBaseURL, url.PathEscape(strings.TrimSpace(name)), url.PathEscape(strings.TrimSpace(action)))
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, nil)
	if err != nil {
		return err
	}
	req.Header.Set("X-Api-Key", c.apiKey)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusForbidden || resp.StatusCode == http.StatusUnauthorized {
		return errors.New("invalid API key")
	}
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return fmt.Errorf("server action failed: %s", readBody(resp.Body))
	}

	return nil
}

func readBody(reader io.Reader) string {
	if reader == nil {
		return ""
	}
	body, err := io.ReadAll(reader)
	if err != nil {
		return ""
	}
	text := strings.TrimSpace(string(body))
	if text == "" {
		return "(empty response)"
	}
	return text
}
