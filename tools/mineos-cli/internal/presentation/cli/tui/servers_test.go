package tui

import (
	"strings"
	"testing"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

func TestFormatPlayers(t *testing.T) {
	i := func(n int) *int { return &n }
	cases := []struct {
		on, mx *int
		want   string
	}{
		{nil, nil, "—"},
		{i(3), i(20), "3/20"},
		{i(5), nil, "5"},
	}
	for _, c := range cases {
		if got := formatPlayers(c.on, c.mx); got != c.want {
			t.Errorf("formatPlayers = %q, want %q", got, c.want)
		}
	}
}

func TestFormatMemory(t *testing.T) {
	b := func(n int64) *int64 { return &n }
	cases := []struct {
		in   *int64
		want string
	}{
		{nil, "—"},
		{b(0), "—"},
		{b(536870912), "512M"},
		{b(2 * 1024 * 1024 * 1024), "2.0G"},
	}
	for _, c := range cases {
		if got := formatMemory(c.in); got != c.want {
			t.Errorf("formatMemory = %q, want %q", got, c.want)
		}
	}
}

func TestRenderMetricsLines(t *testing.T) {
	// No sample yet → a waiting line.
	m := TuiModel{}
	if got := strings.Join(m.renderMetricsLines(), "\n"); !strings.Contains(got, "waiting") {
		t.Fatalf("expected a waiting line, got %q", got)
	}

	// With a sample → TPS/players are rendered.
	tps := 19.9
	m.PerfSample = &api.PerfSample{IsRunning: true, CpuPercent: 12, RamUsedMb: 512, RamTotalMb: 1024, Tps: &tps, PlayerCount: 3}
	got := strings.Join(m.renderMetricsLines(), "\n")
	for _, want := range []string{"TPS", "19.9", "Players: 3"} {
		if !strings.Contains(got, want) {
			t.Fatalf("metrics panel missing %q in %q", want, got)
		}
	}
}
