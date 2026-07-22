package tui

import (
	"strings"
	"testing"

	tea "github.com/charmbracelet/bubbletea"
)

func TestUpdate_StatusAndErrTTLSweep(t *testing.T) {
	m := TuiModel{ConnectionState: ConnectionState{ConfigReady: true}, StatusState: StatusState{StatusMsg: "Restart complete", ErrMsg: "boom"}}

	// First tick: messages survive (just seen).
	out, _ := m.Update(HealthTickMsg{})
	m = out.(TuiModel)
	if m.StatusMsg == "" || m.ErrMsg == "" {
		t.Fatal("messages must survive their first tick")
	}

	// Second tick with no change: swept.
	out, _ = m.Update(HealthTickMsg{})
	m = out.(TuiModel)
	if m.StatusMsg != "" || m.ErrMsg != "" {
		t.Fatalf("stale messages must be cleared, got %q / %q", m.StatusMsg, m.ErrMsg)
	}
}

func TestUpdate_TTLSweepKeepsRefreshedMessages(t *testing.T) {
	m := TuiModel{ConnectionState: ConnectionState{ConfigReady: true}, StatusState: StatusState{ErrMsg: "invalid API key"}}
	out, _ := m.Update(HealthTickMsg{})
	m = out.(TuiModel)

	// A persistent condition re-sets the message between ticks.
	m.ErrMsg = "invalid API key (again)"
	out, _ = m.Update(HealthTickMsg{})
	m = out.(TuiModel)
	if m.ErrMsg == "" {
		t.Fatal("a re-set message must survive the sweep")
	}
}

func TestUpdate_StreamingFinishedUsesDeclaredEffect(t *testing.T) {
	// A stop effect marks containers stopped regardless of label wording.
	m := TuiModel{ConnectionState: ConnectionState{ConfigReady: true}, ServerListState: ServerListState{Servers: serverList("lobby")}}
	out, _ := m.Update(StreamingFinishedMsg{Label: "Anything", Effect: EffectStopsContainers})
	got := out.(TuiModel)
	if !got.ContainersStopped || got.ConfigReady || got.Servers != nil {
		t.Fatalf("stop effect not applied: %+v", got)
	}

	// A start effect clears the stopped flag.
	out, _ = got.Update(StreamingFinishedMsg{Label: "Anything", Effect: EffectStartsContainers})
	if out.(TuiModel).ContainersStopped {
		t.Fatal("start effect must clear ContainersStopped")
	}

	// A failed command applies no effect.
	m = TuiModel{ConnectionState: ConnectionState{ConfigReady: true}}
	out, _ = m.Update(StreamingFinishedMsg{Effect: EffectStopsContainers, Err: errFake})
	if out.(TuiModel).ContainersStopped {
		t.Fatal("a failed command must not change container state")
	}
}

func TestHandleKey_HelpOverlayToggles(t *testing.T) {
	m := TuiModel{}
	key := tea.KeyMsg{Type: tea.KeyRunes, Runes: []rune("?")}

	out, _ := m.Update(key)
	m = out.(TuiModel)
	if !m.ShowHelp {
		t.Fatal("? must open the help overlay")
	}

	// Any key closes it.
	out, _ = m.Update(tea.KeyMsg{Type: tea.KeyRunes, Runes: []rune("j")})
	m = out.(TuiModel)
	if m.ShowHelp {
		t.Fatal("any key must close the help overlay")
	}

	// q still quits from inside the overlay.
	m.ShowHelp = true
	out, cmd := m.Update(tea.KeyMsg{Type: tea.KeyRunes, Runes: []rune("q")})
	if !out.(TuiModel).Quitting || cmd == nil {
		t.Fatal("q must quit even with help open")
	}
}

func TestRenderHelpOverlay_ListsBindings(t *testing.T) {
	m := TuiModel{}
	joined := strings.Join(m.RenderHelpOverlay(80, 30), "\n")
	for _, want := range []string{"KEYBINDINGS", "j/k", "Search logs", "Quit"} {
		if !strings.Contains(joined, want) {
			t.Fatalf("help overlay missing %q", want)
		}
	}
}

func TestRenderServersTable_DistinguishesStates(t *testing.T) {
	width, height := 80, 10

	// Loading (API not ready)
	m := TuiModel{}
	if !containsLine(m.RenderServersTable(width, height), "Connecting to API") {
		t.Fatal("expected connecting state")
	}

	// Connected, list not loaded yet
	m = TuiModel{ConnectionState: ConnectionState{ConfigReady: true}}
	if !containsLine(m.RenderServersTable(width, height), "Loading servers") {
		t.Fatal("expected loading state")
	}

	// Unreachable (error set)
	m = TuiModel{ConnectionState: ConnectionState{ConfigReady: true}, StatusState: StatusState{ErrMsg: "connection refused"}}
	if !containsLine(m.RenderServersTable(width, height), "Server list unavailable") {
		t.Fatal("expected unavailable state")
	}

	// Containers intentionally stopped
	m = TuiModel{ConnectionState: ConnectionState{ContainersStopped: true}}
	if !containsLine(m.RenderServersTable(width, height), "Containers are stopped") {
		t.Fatal("expected stopped state")
	}

	// Genuinely empty
	m = TuiModel{ConnectionState: ConnectionState{ConfigReady: true}, ServerListState: ServerListState{ServersLoadedOnce: true}}
	if !containsLine(m.RenderServersTable(width, height), "No servers found") {
		t.Fatal("expected empty state")
	}
}
