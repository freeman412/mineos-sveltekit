package tui

import (
	"strings"
	"testing"
	"time"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

func TestUpdate_HealthDataMsgPopulatesModel(t *testing.T) {
	m := TuiModel{}
	msg := HealthDataMsg{
		Watchdog: map[string]api.WatchdogServerStatus{"lobby": {ServerName: "lobby", IsMonitoring: true}},
		Crashes:  []api.CrashEvent{{ServerName: "lobby", CrashType: "OutOfMemory"}},
		Alerts:   []api.Notification{{Title: "Low TPS"}},
	}
	out, _ := m.Update(msg)
	got := out.(TuiModel)
	if !got.HealthDataLoaded {
		t.Fatal("HealthDataLoaded should be true after a successful fetch")
	}
	if len(got.Watchdog) != 1 || len(got.Crashes) != 1 || len(got.Alerts) != 1 {
		t.Fatalf("data not stored: %+v", got)
	}
}

func TestUpdate_HealthDataMsgErrorKeepsOldData(t *testing.T) {
	m := TuiModel{HealthDataLoaded: true, Crashes: []api.CrashEvent{{ServerName: "lobby"}}}
	out, _ := m.Update(HealthDataMsg{Err: errFake})
	got := out.(TuiModel)
	if got.HealthDataErr == "" {
		t.Fatal("error should be surfaced")
	}
	if len(got.Crashes) != 1 {
		t.Fatal("previous data must survive a failed refresh")
	}
}

func TestRenderHealthMain_States(t *testing.T) {
	// Not connected
	m := TuiModel{}
	if !containsLine(m.RenderHealthMain(80, 20), "API not connected") {
		t.Fatal("expected not-connected state")
	}

	// Connected but still loading
	m.ConfigReady = true
	if !containsLine(m.RenderHealthMain(80, 20), "Loading health data") {
		t.Fatal("expected loading state")
	}

	// Loaded and empty
	m.HealthDataLoaded = true
	lines := m.RenderHealthMain(80, 30)
	if !containsLine(lines, "No servers monitored") || !containsLine(lines, "No crashes recorded") || !containsLine(lines, "No active alerts") {
		t.Fatalf("expected empty sections, got:\n%s", strings.Join(lines, "\n"))
	}

	// Populated
	crashTime := time.Now().Add(-5 * time.Minute)
	server := "survival"
	m.Watchdog = map[string]api.WatchdogServerStatus{
		"survival": {ServerName: "survival", IsMonitoring: true, RestartAttempts: 2, LastCrashTime: &crashTime},
	}
	m.Crashes = []api.CrashEvent{{ServerName: "survival", DetectedAt: crashTime, CrashType: "OutOfMemory", AutoRestartAttempted: true, AutoRestartSucceeded: true}}
	m.Alerts = []api.Notification{{Type: "warning", Title: "Low TPS", CreatedAt: crashTime, ServerName: &server}}
	lines = m.RenderHealthMain(80, 30)
	joined := strings.Join(lines, "\n")
	for _, want := range []string{"survival", "monitoring", "restarts: 2", "OutOfMemory", "Low TPS", "5m ago"} {
		if !strings.Contains(joined, want) {
			t.Fatalf("missing %q in:\n%s", want, joined)
		}
	}
}

func TestFormatDuration(t *testing.T) {
	cases := []struct {
		d    time.Duration
		want string
	}{
		{30 * time.Second, "30s"},
		{5 * time.Minute, "5m"},
		{3 * time.Hour, "3h"},
		{48 * time.Hour, "2d"},
	}
	for _, c := range cases {
		if got := formatDuration(c.d); got != c.want {
			t.Fatalf("formatDuration(%v) = %q, want %q", c.d, got, c.want)
		}
	}
}

func containsLine(lines []string, substr string) bool {
	for _, l := range lines {
		if strings.Contains(l, substr) {
			return true
		}
	}
	return false
}

var errFake = &fakeError{}

type fakeError struct{}

func (*fakeError) Error() string { return "boom" }
