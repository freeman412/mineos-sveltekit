package tui

import (
	"fmt"
	"strings"
)

// RenderServiceLogsMain renders the combined service logs view (Docker containers)
func (m TuiModel) RenderServiceLogsMain(width, height int) []string {
	lines := make([]string, 0, height)

	// Header
	lines = append(lines, StyleHeader.Render(" SERVICE LOGS "))
	lines = append(lines, StyleSubtle.Render(strings.Repeat("─", width)))

	// Service selector
	if len(m.ComposeServices) > 1 {
		svcLine := "  Viewing: "
		for i, svc := range m.ComposeServices {
			if svc == m.LogSource {
				svcLine += StyleSelected.Render("[" + svc + "]")
			} else {
				svcLine += StyleSubtle.Render(" " + svc + " ")
			}
			if i < len(m.ComposeServices)-1 {
				svcLine += " "
			}
		}
		lines = append(lines, TrimToWidth(svcLine, width))
		lines = append(lines, StyleSubtle.Render("  Use ← → to switch services"))
		lines = append(lines, "")
	}

	// Log content
	title := fmt.Sprintf(" LOGS: %s ", m.LogSource)
	lines = append(lines, StyleHeader.Render(title))

	if !m.ComposeReady {
		lines = append(lines, "")
		lines = append(lines, StyleError.Render("  Docker Compose not available."))
		if m.ComposeError != "" {
			lines = append(lines, "  "+m.ComposeError)
		}
		return PadLines(lines, height)
	}

	if !m.LogsActive {
		lines = append(lines, TrimToWidth(StyleSubtle.Render("  Logs paused."), width))
		return PadLines(lines, height)
	}

	if len(m.Logs) == 0 {
		if m.ComposeError != "" {
			lines = append(lines, TrimToWidth(StyleError.Render("  Error: "+m.ComposeError), width))
		} else {
			lines = append(lines, TrimToWidth(StyleSubtle.Render("  Waiting for logs..."), width))
		}
		return PadLines(lines, height)
	}

	// Show logs
	usedHeight := len(lines)
	logHeight := height - usedHeight
	start := 0
	if len(m.Logs) > logHeight {
		start = len(m.Logs) - logHeight
	}
	for _, line := range m.Logs[start:] {
		lines = append(lines, TrimToWidth("  "+line, width))
	}

	return PadLines(lines, height)
}
