package api

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"strings"
	"time"
)

// WatchdogServerStatus mirrors the API's ServerWatchdogStatus record.
type WatchdogServerStatus struct {
	ServerName         string     `json:"serverName"`
	IsMonitoring       bool       `json:"isMonitoring"`
	WasRunning         bool       `json:"wasRunning"`
	RestartAttempts    int        `json:"restartAttempts"`
	LastCrashTime      *time.Time `json:"lastCrashTime"`
	LastManualStopTime *time.Time `json:"lastManualStopTime"`
	LastRestartAttempt *time.Time `json:"lastRestartAttempt"`
	CooldownEndsAt     *time.Time `json:"cooldownEndsAt"`
}

// CrashEvent mirrors the API's CrashEventDto.
type CrashEvent struct {
	Id                   int       `json:"id"`
	ServerName           string    `json:"serverName"`
	DetectedAt           time.Time `json:"detectedAt"`
	CrashType            string    `json:"crashType"` // ProcessDeath, CrashReport, OutOfMemory, Timeout
	CrashDetails         *string   `json:"crashDetails"`
	AutoRestartAttempted bool      `json:"autoRestartAttempted"`
	AutoRestartSucceeded bool      `json:"autoRestartSucceeded"`
}

// Notification mirrors the API's SystemNotification entity (the fields the CLI shows).
type Notification struct {
	Id         int       `json:"id"`
	Type       string    `json:"type"` // info, warning, error, success
	Title      string    `json:"title"`
	Message    string    `json:"message"`
	CreatedAt  time.Time `json:"createdAt"`
	IsRead     bool      `json:"isRead"`
	ServerName *string   `json:"serverName"`
}

// getJSON performs an authenticated GET and decodes the JSON response into out.
func (c *Client) getJSON(ctx context.Context, rawURL string, out any) error {
	if strings.TrimSpace(c.apiKey) == "" {
		return ErrApiKeyMissing
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, rawURL, nil)
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
		return fmt.Errorf("request failed: %s", readBody(resp.Body))
	}
	return json.NewDecoder(resp.Body).Decode(out)
}

// WatchdogStatus fetches the watchdog roll-up for all monitored servers.
// The global watchdog group lives at /api/watchdog, outside /api/v1.
func (c *Client) WatchdogStatus(ctx context.Context) (map[string]WatchdogServerStatus, error) {
	var status map[string]WatchdogServerStatus
	if err := c.getJSON(ctx, c.baseURL+"/api/watchdog/status", &status); err != nil {
		return nil, err
	}
	return status, nil
}

// WatchdogCrashes fetches the most recent crash events across all servers.
func (c *Client) WatchdogCrashes(ctx context.Context, limit int) ([]CrashEvent, error) {
	url := fmt.Sprintf("%s/api/watchdog/crashes?limit=%d", c.baseURL, limit)
	var events []CrashEvent
	if err := c.getJSON(ctx, url, &events); err != nil {
		return nil, err
	}
	return events, nil
}

// ActiveNotifications fetches non-dismissed notifications (newest first).
func (c *Client) ActiveNotifications(ctx context.Context) ([]Notification, error) {
	var notifications []Notification
	if err := c.getJSON(ctx, c.apiBaseURL+"/notifications?includeDismissed=false", &notifications); err != nil {
		return nil, err
	}
	return notifications, nil
}
