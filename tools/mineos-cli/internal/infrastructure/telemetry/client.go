package telemetry

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"runtime"
	"time"

	"github.com/google/uuid"
)

// geoInfo holds geographic data from an IP geolocation lookup.
type geoInfo struct {
	Country  string `json:"country"`
	Region   string `json:"regionName"`
	City     string `json:"city"`
	Timezone string `json:"timezone"`
}

// lookupGeo queries a free IP geolocation API and returns the result.
// Returns nil on any error so callers can safely ignore failures.
func lookupGeo() *geoInfo {
	client := &http.Client{Timeout: 5 * time.Second}
	resp, err := client.Get("http://ip-api.com/json/?fields=country,regionName,city,timezone")
	if err != nil {
		return nil
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return nil
	}
	var geo geoInfo
	if err := json.NewDecoder(resp.Body).Decode(&geo); err != nil {
		return nil
	}
	return &geo
}

type Client struct {
	baseURL      string
	httpClient   *http.Client
	enabled      bool
	telemetryKey string
}

func NewClient(baseURL string, enabled bool, telemetryKey string) *Client {
	return &Client{
		baseURL: baseURL,
		httpClient: &http.Client{
			Timeout: 10 * time.Second,
		},
		enabled:      enabled,
		telemetryKey: telemetryKey,
	}
}

// InstallResponse represents the response from the /api/telemetry/install endpoint.
type InstallResponse struct {
	TelemetryKey string `json:"telemetry_key"`
}

// InstallEvent represents installation telemetry
type InstallEvent struct {
	InstallationID    string  `json:"installation_id"`
	Country           *string `json:"country,omitempty"`
	Region            *string `json:"region,omitempty"`
	City              *string `json:"city,omitempty"`
	Timezone          *string `json:"timezone,omitempty"`
	OS                string  `json:"os"`
	OSVersion         *string `json:"os_version,omitempty"`
	Architecture      string  `json:"architecture"`
	Locale            *string `json:"locale,omitempty"`
	MineOSVersion     string  `json:"mineos_version"`
	InstallMethod     string  `json:"install_method"`
	InstallSuccess    bool    `json:"install_success"`
	InstallDurationMs *int64  `json:"install_duration_ms,omitempty"`
	ErrorMessage      *string `json:"error_message,omitempty"`
	Referrer          *string `json:"referrer,omitempty"`
	UserAgent         string  `json:"user_agent"`
	IsDocker          bool    `json:"is_docker"`
	CPUCores          *int    `json:"cpu_cores,omitempty"`
	RAMTotalMB        *int64  `json:"ram_total_mb,omitempty"`
	DiskTotalGB       *int64  `json:"disk_total_gb,omitempty"`
}

// UsageEvent represents usage telemetry
type UsageEvent struct {
	InstallationID     string   `json:"installation_id"`
	ServerCount        *int     `json:"server_count,omitempty"`
	ActiveServerCount  *int     `json:"active_server_count,omitempty"`
	TotalUserCount     *int     `json:"total_user_count,omitempty"`
	ActiveUserCount    *int     `json:"active_user_count,omitempty"`
	MinecraftUsernames []string `json:"minecraft_usernames,omitempty"`
	UptimeSeconds      *int64   `json:"uptime_seconds,omitempty"`
	CommandsRun        *int     `json:"commands_run,omitempty"`
	MineOSVersion      string   `json:"mineos_version"`
}

// ReportInstall sends installation telemetry and returns the install response
// containing the telemetry_key.
func (c *Client) ReportInstall(ctx context.Context, event InstallEvent) (*InstallResponse, error) {
	if !c.enabled {
		return nil, nil
	}

	data, err := json.Marshal(event)
	if err != nil {
		return nil, fmt.Errorf("failed to marshal payload: %w", err)
	}

	req, err := http.NewRequestWithContext(ctx, "POST", c.baseURL+"/api/telemetry/install", bytes.NewReader(data))
	if err != nil {
		return nil, fmt.Errorf("failed to create request: %w", err)
	}

	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("User-Agent", fmt.Sprintf("MineOS-CLI/%s (%s; %s)", "dev", runtime.GOOS, runtime.GOARCH))

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("failed to send request: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return nil, fmt.Errorf("telemetry request failed with status: %s", resp.Status)
	}

	var installResp InstallResponse
	if err := json.NewDecoder(resp.Body).Decode(&installResp); err != nil {
		return nil, nil
	}

	return &installResp, nil
}

// ReportUsage sends usage telemetry
func (c *Client) ReportUsage(ctx context.Context, event UsageEvent) error {
	if !c.enabled {
		return nil
	}

	return c.post(ctx, "/api/telemetry/usage", event)
}

func (c *Client) post(ctx context.Context, path string, payload interface{}) error {
	data, err := json.Marshal(payload)
	if err != nil {
		return fmt.Errorf("failed to marshal payload: %w", err)
	}

	req, err := http.NewRequestWithContext(ctx, "POST", c.baseURL+path, bytes.NewReader(data))
	if err != nil {
		return fmt.Errorf("failed to create request: %w", err)
	}

	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("User-Agent", fmt.Sprintf("MineOS-CLI/%s (%s; %s)", "dev", runtime.GOOS, runtime.GOARCH))

	if c.telemetryKey != "" {
		req.Header.Set("Authorization", "Bearer "+c.telemetryKey)
	}

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return fmt.Errorf("failed to send request: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return fmt.Errorf("telemetry request failed with status: %s", resp.Status)
	}

	return nil
}

// GenerateInstallationID creates a new UUID for tracking installations
func GenerateInstallationID() string {
	return uuid.New().String()
}

// BuildInstallEvent creates an InstallEvent with system information
func BuildInstallEvent(installationID, version string, success bool, durationMs int64, errorMsg string) InstallEvent {
	cores := runtime.NumCPU()
	osName := runtime.GOOS
	arch := runtime.GOARCH

	event := InstallEvent{
		InstallationID:    installationID,
		OS:                osName,
		Architecture:      arch,
		MineOSVersion:     version,
		InstallMethod:     "cli",
		InstallSuccess:    success,
		InstallDurationMs: &durationMs,
		UserAgent:         fmt.Sprintf("MineOS-Installer/%s", version),
		IsDocker:          true,
		CPUCores:          &cores,
	}

	if errorMsg != "" {
		event.ErrorMessage = &errorMsg
	}

	if geo := lookupGeo(); geo != nil {
		if geo.Country != "" {
			event.Country = &geo.Country
		}
		if geo.Region != "" {
			event.Region = &geo.Region
		}
		if geo.City != "" {
			event.City = &geo.City
		}
		if geo.Timezone != "" {
			event.Timezone = &geo.Timezone
		}
	}

	return event
}
