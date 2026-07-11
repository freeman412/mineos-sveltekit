package tui

import (
	"strings"
	"testing"
)

// A terminal narrower than the sidebar used to drive the content width negative
// and panic strings.Repeat. View must degrade to a resize notice instead.
func TestView_SmallTerminal_DoesNotPanic(t *testing.T) {
	sizes := []struct{ w, h int }{
		{0, 0},   // pre-size: existing "Loading..." path
		{1, 1},   // absurdly small
		{15, 4},  // narrower than SidebarWidth — the historical panic case
		{MinTerminalWidth - 1, MinTerminalHeight - 1}, // just under the threshold
	}
	for _, s := range sizes {
		m := TuiModel{Width: s.w, Height: s.h}
		// Must not panic.
		_ = m.View()
	}
}

func TestView_TooSmall_ShowsResizeNotice(t *testing.T) {
	m := TuiModel{Width: MinTerminalWidth - 1, Height: MinTerminalHeight - 1}
	out := m.View()
	if !strings.Contains(out, "too small") {
		t.Fatalf("expected a resize notice for a too-small terminal, got %q", out)
	}
}
