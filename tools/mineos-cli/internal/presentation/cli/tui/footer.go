package tui

import (
	"strings"

	"github.com/charmbracelet/lipgloss"
)

func (m TuiModel) RenderFooter() string {
	var b strings.Builder

	// Command/Status line
	if m.Mode == ModeCommand {
		b.WriteString(StyleStatus.Render(" CONSOLE: "))
		b.WriteString(m.Input.View())
	} else if m.ErrMsg != "" {
		b.WriteString(TrimToWidth(StyleError.Render(" ERROR: "+m.ErrMsg), m.Width))
	} else if m.StatusMsg != "" {
		b.WriteString(TrimToWidth(StyleStatus.Render(" "+m.StatusMsg), m.Width))
	} else {
		b.WriteString(StyleSubtle.Render(" "))
	}
	b.WriteString("\n")

	// Keyboard shortcuts line - context-sensitive
	help := " [Up/Down] Navigate  [Enter] Select  [Esc] Back  [q] Quit"
	if m.CurrentView == ViewServiceLogs && len(m.ComposeServices) > 1 {
		help = " [Up/Down] Navigate  [Left/Right] Switch Service  [Esc] Back  [q] Quit"
	}

	footerStyle := lipgloss.NewStyle().
		Background(lipgloss.Color("235")).
		Foreground(lipgloss.Color("245")).
		Width(m.Width)

	return "\n" + b.String() + footerStyle.Render(help)
}
