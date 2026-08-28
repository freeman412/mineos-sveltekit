package tui

import (
	"testing"

	tea "github.com/charmbracelet/bubbletea"
)

func actionIndexOf(t *testing.T, action string) int {
	t.Helper()
	for i, a := range GetServerActions() {
		if a.Action == action {
			return i
		}
	}
	t.Fatalf("action %q not found", action)
	return -1
}

func serversActionModel(action string, t *testing.T) TuiModel {
	return TuiModel{ServerListState: ServerListState{Servers: serverList("lobby"), ServerActions: true, ActionIndex: actionIndexOf(t, action)}, NavState: NavState{CurrentView: ViewServers}}
}

func TestServerAction_RunsInProcessWithoutOutputView(t *testing.T) {
	m := serversActionModel("start", t)
	out, cmd := m.Update(tea.KeyMsg{Type: tea.KeyEnter})
	got := out.(TuiModel)

	if got.CurrentView != ViewServers {
		t.Fatal("server actions must not detour through the output view")
	}
	if cmd == nil {
		t.Fatal("expected an in-process action command")
	}
	if got.StatusMsg == "" {
		t.Fatal("dispatch must set a status line")
	}

	// With no client connected the command reports a clean error message.
	msg := cmd()
	done, ok := msg.(ServerActionDoneMsg)
	if !ok {
		t.Fatalf("want ServerActionDoneMsg, got %T", msg)
	}
	if done.Server != "lobby" || done.Action != "start" || done.Err == nil {
		t.Fatalf("unexpected result: %+v", done)
	}
}

func TestServerAction_KillRequiresConfirmThenRunsInProcess(t *testing.T) {
	m := serversActionModel("kill", t)
	out, _ := m.Update(tea.KeyMsg{Type: tea.KeyEnter})
	got := out.(TuiModel)

	if got.Mode != ModeConfirm || got.ConfirmServerAction != "kill" || got.ConfirmServerName != "lobby" {
		t.Fatalf("kill must confirm first: %+v", got)
	}
	if got.ConfirmAction != nil {
		t.Fatal("in-process confirmation must not carry a subprocess MenuItem")
	}

	// Confirming dispatches the in-process action and clears confirm state.
	out, cmd := got.Update(tea.KeyMsg{Type: tea.KeyRunes, Runes: []rune("y")})
	got = out.(TuiModel)
	if cmd == nil {
		t.Fatal("confirm must dispatch the action")
	}
	if got.Mode != ModeNormal || got.ConfirmServerAction != "" {
		t.Fatal("confirm state must be cleared")
	}
	if done, ok := cmd().(ServerActionDoneMsg); !ok || done.Action != "kill" {
		t.Fatalf("expected in-process kill, got %v", cmd())
	}
}

func TestServerAction_ConfirmCancelClearsState(t *testing.T) {
	m := serversActionModel("kill", t)
	out, _ := m.Update(tea.KeyMsg{Type: tea.KeyEnter})
	got := out.(TuiModel)

	out, cmd := got.Update(tea.KeyMsg{Type: tea.KeyEsc})
	got = out.(TuiModel)
	if cmd != nil || got.Mode != ModeNormal || got.ConfirmServerAction != "" || got.ConfirmServerName != "" {
		t.Fatalf("cancel must clear pending action: %+v", got)
	}
}

func TestUpdate_ServerActionDone(t *testing.T) {
	m := TuiModel{}
	out, cmd := m.Update(ServerActionDoneMsg{Server: "lobby", Action: "start"})
	got := out.(TuiModel)
	if got.StatusMsg == "" || got.ErrMsg != "" {
		t.Fatalf("success must set status: %+v", got)
	}
	if cmd == nil {
		t.Fatal("completion must refresh the server list")
	}

	out, _ = m.Update(ServerActionDoneMsg{Server: "lobby", Action: "stop", Err: errFake})
	if out.(TuiModel).ErrMsg == "" {
		t.Fatal("failure must surface the error")
	}
}
