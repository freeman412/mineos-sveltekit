package tui

import "strings"

// helpEntry is one key/description row in the help overlay.
type helpEntry struct {
	Keys string
	Desc string
}

// helpSections lists every TUI keybinding, grouped for the overlay.
func helpSections() []struct {
	Title   string
	Entries []helpEntry
} {
	return []struct {
		Title   string
		Entries []helpEntry
	}{
		{"Navigation", []helpEntry{
			{"↑/↓, j/k", "Move selection / scroll logs"},
			{"←/→, h/l", "Switch log source (Service Logs)"},
			{"Enter", "Select / open server actions"},
			{"Esc", "Back / close"},
			{"PgUp/PgDn", "Page through logs"},
			{"g / G", "Jump to top / bottom of logs"},
		}},
		{"Logs", []helpEntry{
			{"/", "Search logs (Service Logs)"},
			{"n / N", "Next / previous match"},
		}},
		{"Other", []helpEntry{
			{"p", "Toggle pre-release channel (Settings)"},
			{"?", "Toggle this help"},
			{"q, Ctrl+C", "Quit"},
		}},
	}
}

// RenderHelpOverlay renders the keybinding reference as a full-pane overlay.
func (m TuiModel) RenderHelpOverlay(width, height int) []string {
	lines := make([]string, 0, height)
	lines = append(lines, StyleHeader.Render(" KEYBINDINGS "))
	lines = append(lines, StyleSubtle.Render(strings.Repeat("─", width)))

	for _, section := range helpSections() {
		lines = append(lines, "")
		lines = append(lines, StyleHeader.Render(" "+section.Title+" "))
		for _, e := range section.Entries {
			lines = append(lines, TrimToWidth("  "+StyleStatus.Render(padKey(e.Keys))+" "+e.Desc, width))
		}
	}

	lines = append(lines, "")
	lines = append(lines, StyleSubtle.Render("  Press ? or Esc to close."))
	return PadLines(lines, height)
}

// padKey left-aligns key labels into a fixed column.
func padKey(keys string) string {
	const col = 12
	if len(keys) >= col {
		return keys
	}
	return keys + strings.Repeat(" ", col-len(keys))
}
