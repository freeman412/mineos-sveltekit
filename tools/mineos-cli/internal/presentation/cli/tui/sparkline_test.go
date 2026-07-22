package tui

import (
	"strings"
	"testing"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/ports"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

func TestSparkline_ScalesMinToMax(t *testing.T) {
	got := Sparkline([]float64{0, 50, 100}, 3)
	runes := []rune(got)
	if len(runes) != 3 {
		t.Fatalf("want 3 runes, got %d (%q)", len(runes), got)
	}
	if runes[0] != '▁' || runes[2] != '█' {
		t.Fatalf("min/max not at extremes: %q", got)
	}
}

func TestSparkline_FlatSeriesRendersMidLevel(t *testing.T) {
	got := Sparkline([]float64{20, 20, 20}, 3)
	if strings.ContainsAny(got, "▁█") {
		t.Fatalf("flat series must not render extremes: %q", got)
	}
}

func TestSparkline_DownsamplesToWidth(t *testing.T) {
	values := make([]float64, 100)
	for i := range values {
		values[i] = float64(i)
	}
	got := Sparkline(values, 10)
	if len([]rune(got)) != 10 {
		t.Fatalf("want 10 runes, got %d", len([]rune(got)))
	}
}

func TestSparkline_EmptyAndZeroWidth(t *testing.T) {
	if Sparkline(nil, 10) != "" || Sparkline([]float64{1}, 0) != "" {
		t.Fatal("empty series or zero width must render empty")
	}
}

func TestSeriesStats(t *testing.T) {
	lo, avg, hi := SeriesStats([]float64{10, 20, 30})
	if lo != 10 || avg != 20 || hi != 30 {
		t.Fatalf("got %v %v %v", lo, avg, hi)
	}
	lo, avg, hi = SeriesStats(nil)
	if lo != 0 || avg != 0 || hi != 0 {
		t.Fatal("empty series must yield zeros")
	}
}

func TestUpdate_PerfHistoryBackfillsAndGuardsServer(t *testing.T) {
	tps := 19.5
	m := TuiModel{
		Servers:  serverList("lobby"),
		Selected: 0,
	}
	// Live sample arrived first
	out, _ := m.Update(PerfSampleMsg{Sample: api.PerfSample{CpuPercent: 50}})
	m = out.(TuiModel)

	// Backfill for the selected server goes in front of live samples
	out, _ = m.Update(PerfHistoryMsg{Server: "lobby", Samples: []api.PerfSample{{CpuPercent: 10, Tps: &tps}}})
	m = out.(TuiModel)
	if len(m.PerfHistory) != 2 || m.PerfHistory[0].CpuPercent != 10 {
		t.Fatalf("history not prepended: %+v", m.PerfHistory)
	}

	// Backfill for a stale selection is ignored
	out, _ = m.Update(PerfHistoryMsg{Server: "other", Samples: []api.PerfSample{{CpuPercent: 99}}})
	m = out.(TuiModel)
	if len(m.PerfHistory) != 2 {
		t.Fatal("stale-server backfill must be ignored")
	}
}

func TestAppendCapped_DropsOldest(t *testing.T) {
	var history []api.PerfSample
	for i := 0; i < 5; i++ {
		history = appendCapped(history, api.PerfSample{CpuPercent: float64(i)}, 3)
	}
	if len(history) != 3 || history[0].CpuPercent != 2 {
		t.Fatalf("cap not enforced: %+v", history)
	}
}

func TestRenderSparklines_NeedsTwoSamples(t *testing.T) {
	m := TuiModel{PerfHistory: []api.PerfSample{{CpuPercent: 10}}}
	if lines := m.renderSparklines(); lines != nil {
		t.Fatal("one sample must not render a sparkline")
	}
	m.PerfHistory = append(m.PerfHistory, api.PerfSample{CpuPercent: 90})
	lines := m.renderSparklines()
	if len(lines) != 1 || !strings.Contains(lines[0], "CPU") {
		t.Fatalf("expected a CPU strip, got %v", lines)
	}
}

// serverList builds a minimal server slice for selection tests.
func serverList(names ...string) []ports.Server {
	servers := make([]ports.Server, len(names))
	for i, n := range names {
		servers[i] = ports.Server{Name: n}
	}
	return servers
}
