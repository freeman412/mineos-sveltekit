package tui

import (
	"testing"

	tea "github.com/charmbracelet/bubbletea"
)

func navIndexOf(t *testing.T, items []NavItem, label string) int {
	t.Helper()
	for i, item := range items {
		if item.Label == label {
			return i
		}
	}
	t.Fatalf("nav item %q not found", label)
	return -1
}

func TestNavSelect_SwitchesToView(t *testing.T) {
	items := BuildNavItems()
	m := TuiModel{NavItems: items, CurrentView: ViewDashboard}
	m.NavIndex = navIndexOf(t, items, "Health & Alerts")

	out, _ := m.Update(tea.KeyMsg{Type: tea.KeyEnter})
	got := out.(TuiModel)
	if got.CurrentView != ViewHealth {
		t.Fatalf("want ViewHealth, got %v", got.CurrentView)
	}
	if got.PreviousView != ViewDashboard {
		t.Fatal("previous view not recorded")
	}
}

func TestNavSelect_EnterOnServerOpensActions(t *testing.T) {
	m := TuiModel{CurrentView: ViewServers, Servers: serverList("lobby", "survival"), Selected: 1}
	out, _ := m.Update(tea.KeyMsg{Type: tea.KeyEnter})
	got := out.(TuiModel)
	if !got.ServerActions || got.ActionIndex != 0 {
		t.Fatalf("server actions not opened: %+v", got)
	}
}

func TestNavBack_LeavesServerActionsAndClearsPerfState(t *testing.T) {
	m := TuiModel{
		CurrentView:   ViewServers,
		Servers:       serverList("lobby"),
		ServerActions: true,
		ActionIndex:   2,
		PerfServer:    "lobby",
	}
	out, _ := m.Update(tea.KeyMsg{Type: tea.KeyEsc})
	got := out.(TuiModel)
	if got.ServerActions || got.ActionIndex != 0 {
		t.Fatal("Esc must leave server-actions mode")
	}
	if got.PerfServer != "" || got.PerfHistory != nil {
		t.Fatal("perf stream state must be cleared on exit")
	}
}

func TestNavBack_OutputReturnsToPreviousView(t *testing.T) {
	m := TuiModel{CurrentView: ViewOutput, PreviousView: ViewServers}
	out, _ := m.Update(tea.KeyMsg{Type: tea.KeyEsc})
	if out.(TuiModel).CurrentView != ViewServers {
		t.Fatal("Esc from output must return to the previous view")
	}
}

func TestServersNavigation_MovesSelectionAndRetargetsLogs(t *testing.T) {
	m := TuiModel{CurrentView: ViewServers, Servers: serverList("alpha", "beta"), Selected: 0, MinecraftSource: "alpha"}
	out, _ := m.Update(tea.KeyMsg{Type: tea.KeyDown})
	got := out.(TuiModel)
	if got.Selected != 1 || got.MinecraftSource != "beta" {
		t.Fatalf("selection/log source wrong: %+v", got)
	}

	out, _ = got.Update(tea.KeyMsg{Type: tea.KeyUp})
	got = out.(TuiModel)
	if got.Selected != 0 || got.MinecraftSource != "alpha" {
		t.Fatalf("selection did not move back: %+v", got)
	}
}

func TestQuitKeys(t *testing.T) {
	for _, key := range []tea.KeyMsg{
		{Type: tea.KeyCtrlC},
		{Type: tea.KeyRunes, Runes: []rune("q")},
	} {
		m := TuiModel{}
		out, cmd := m.Update(key)
		if !out.(TuiModel).Quitting || cmd == nil {
			t.Fatalf("%v must quit", key)
		}
	}
}
