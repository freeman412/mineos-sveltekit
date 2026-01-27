package api

import (
	"bufio"
	"bytes"
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

var (
	ErrApiKeyMissing = errors.New("api key missing; set MINEOS_API_KEY in .env or provide ApiKey__StaticKey")
	ErrApiKeyInvalid = errors.New("invalid API key")
)

type LogEntry struct {
	Timestamp time.Time `json:"timestamp"`
	Message   string    `json:"message"`
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
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, c.apiBaseURL+"/health", nil)
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
		return nil, ErrApiKeyMissing
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
		return nil, ErrApiKeyInvalid
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
		return ports.StopAllResult{}, ErrApiKeyMissing
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
		return ports.StopAllResult{}, ErrApiKeyInvalid
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
		return ErrApiKeyMissing
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
		return ErrApiKeyInvalid
	}
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return fmt.Errorf("server action failed: %s", readBody(resp.Body))
	}

	return nil
}

func (c *Client) SendConsoleCommand(ctx context.Context, name, command string) error {
	if strings.TrimSpace(c.apiKey) == "" {
		return ErrApiKeyMissing
	}
	if strings.TrimSpace(name) == "" {
		return errors.New("server name is required")
	}
	command = strings.TrimSpace(command)
	if command == "" {
		return errors.New("console command is required")
	}

	url := fmt.Sprintf("%s/servers/%s/console", c.apiBaseURL, url.PathEscape(strings.TrimSpace(name)))
	payload, err := json.Marshal(map[string]string{"command": command})
	if err != nil {
		return err
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(payload))
	if err != nil {
		return err
	}
	req.Header.Set("X-Api-Key", c.apiKey)
	req.Header.Set("Content-Type", "application/json")

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusForbidden || resp.StatusCode == http.StatusUnauthorized {
		return ErrApiKeyInvalid
	}
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return fmt.Errorf("console command failed: %s", readBody(resp.Body))
	}

	return nil
}

func (c *Client) StreamConsoleLogs(ctx context.Context, name, source string) (<-chan LogEntry, <-chan error) {
	logs := make(chan LogEntry)
	errs := make(chan error, 1)

	go func() {
		defer close(logs)
		defer close(errs)

		if strings.TrimSpace(c.apiKey) == "" {
			errs <- ErrApiKeyMissing
			return
		}
		if strings.TrimSpace(name) == "" {
			errs <- errors.New("server name is required")
			return
		}

		url := fmt.Sprintf("%s/servers/%s/console/stream", c.apiBaseURL, url.PathEscape(strings.TrimSpace(name)))
		req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
		if err != nil {
			errs <- err
			return
		}
		if strings.TrimSpace(source) != "" {
			query := req.URL.Query()
			query.Set("source", strings.TrimSpace(source))
			req.URL.RawQuery = query.Encode()
		}
		req.Header.Set("X-Api-Key", c.apiKey)

		streamClient := *c.httpClient
		streamClient.Timeout = 0
		resp, err := streamClient.Do(req)
		if err != nil {
			if ctx.Err() == nil {
				errs <- err
			}
			return
		}
		defer resp.Body.Close()

		if resp.StatusCode == http.StatusForbidden || resp.StatusCode == http.StatusUnauthorized {
			errs <- ErrApiKeyInvalid
			return
		}
		if resp.StatusCode < 200 || resp.StatusCode >= 300 {
			errs <- fmt.Errorf("stream logs failed: %s", readBody(resp.Body))
			return
		}

		scanner := bufio.NewScanner(resp.Body)
		scanner.Buffer(make([]byte, 0, 64*1024), 1024*1024)
		for scanner.Scan() {
			line := scanner.Text()
			if !strings.HasPrefix(line, "data:") {
				continue
			}
			payload := strings.TrimSpace(strings.TrimPrefix(line, "data:"))
			if payload == "" {
				continue
			}
			var entry LogEntry
			if err := json.Unmarshal([]byte(payload), &entry); err != nil {
				continue
			}
			select {
			case logs <- entry:
			case <-ctx.Done():
				return
			}
		}

		if err := scanner.Err(); err != nil && ctx.Err() == nil {
			errs <- err
		}
	}()

	return logs, errs
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
