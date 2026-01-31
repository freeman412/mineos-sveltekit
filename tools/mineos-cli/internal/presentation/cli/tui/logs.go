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

	// Show logs (4 space indent to prevent overlap with nav menu)
	usedHeight := len(lines)
	logHeight := height - usedHeight - 2 // Reserve space for scroll indicator and search hint

	// Calculate visible range based on scroll offset
	totalLogs := len(m.Logs)
	if totalLogs == 0 {
		return PadLines(lines, height)
	}

	// Determine which logs to show
	endIdx := totalLogs - m.LogScroll
	startIdx := endIdx - logHeight
	if startIdx < 0 {
		startIdx = 0
	}
	if endIdx > totalLogs {
		endIdx = totalLogs
	}

	// Render visible logs with search highlighting
	query := strings.ToLower(m.LogSearchQuery)
	for i := startIdx; i < endIdx; i++ {
		line := m.Logs[i]
		// Sanitize log line to remove ANSI codes that cause rendering issues on Linux
		sanitized := SanitizeLogLine(line)

		// Highlight search matches
		if m.LogSearchQuery != "" && strings.Contains(strings.ToLower(sanitized), query) {
			// Simple highlight by adding markers
			sanitized = StyleSelected.Render(sanitized)
		}

		lines = append(lines, TrimToWidth("    "+sanitized, width))
	}

	// Show scroll position and search info
	scrollInfo := ""
	if m.LogScroll > 0 {
		scrollInfo = fmt.Sprintf("  ↑ Scroll: %d/%d lines", m.LogScroll, totalLogs)
	} else {
		scrollInfo = fmt.Sprintf("  Viewing latest (%d total)", totalLogs)
	}
	lines = append(lines, StyleSubtle.Render(scrollInfo))

	// Show search hint or active search
	if m.LogSearchQuery != "" {
		searchInfo := fmt.Sprintf("  Search: %s (n/N to navigate)", m.LogSearchQuery)
		lines = append(lines, StyleStatus.Render(searchInfo))
	} else {
		lines = append(lines, StyleSubtle.Render("  Press / to search, ↑↓ or j/k to scroll, PgUp/PgDn for pages"))
	}

	return PadLines(lines, height)
}
