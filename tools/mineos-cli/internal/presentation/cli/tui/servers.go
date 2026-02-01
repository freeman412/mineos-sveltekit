package tui

import (
	"fmt"
	"strings"
)

// ServerActionItem represents an action available for a server
type ServerActionItem struct {
	Label       string
	Action      string // start, stop, restart, kill, console
	Destructive bool
}

// GetServerActions returns the list of actions available for a server
func GetServerActions() []ServerActionItem {
	return []ServerActionItem{
		{Label: "Start Server", Action: "start"},
		{Label: "Stop Server", Action: "stop"},
		{Label: "Restart Server", Action: "restart"},
		{Label: "Kill Server", Action: "kill", Destructive: true},
		{Label: "Send Console Command", Action: "console"},
		{Label: "← Back to Server List", Action: "back"},
	}
}

func (m TuiModel) RenderServersMain(width, height int) []string {
	// If in server actions mode, show actions for selected server
	if m.ServerActions && len(m.Servers) > 0 {
		return m.RenderServerActionsMain(width, height)
	}

	tableHeight := height / 2
	logHeight := height - tableHeight - 1

	tableLines := m.RenderServersTable(width, tableHeight)
	logLines := m.RenderMinecraftLogs(width, logHeight)

	lines := append(tableLines, StyleSubtle.Render(strings.Repeat("─", width)))
	lines = append(lines, logLines...)
	return lines
}

func (m TuiModel) RenderServersTable(width, height int) []string {
	lines := make([]string, 0, height)

	// Table Header
	header := fmt.Sprintf("  %-25s %-15s", "SERVER NAME", "STATUS")
	lines = append(lines, StyleHeader.Render(header))
	lines = append(lines, StyleSubtle.Render(strings.Repeat("─", width)))

	if m.ErrMsg != "" {
		lines = append(lines, TrimToWidth(StyleError.Render(" Error: "+m.ErrMsg), width))
	}

	if len(m.Servers) == 0 {
		lines = append(lines, TrimToWidth(StyleSubtle.Render(" No servers found."), width))
		return PadLines(lines, height)
	}

	for i, server := range m.Servers {
		prefix := "  "
		name := server.Name
		status := server.Status

		nameStyle := StyleHeader // Default
		if i == m.Selected {
			prefix = StyleSelected.Render("▶ ")
			nameStyle = StyleSelected
		}

		statusFormatted := FormatStatus(status)

		// Align columns
		line := fmt.Sprintf("%s%-25s %-15s", prefix, nameStyle.Render(name), statusFormatted)
		lines = append(lines, TrimToWidth(line, width))
	}

	return PadLines(lines, height)
}

// RenderMinecraftLogs renders Minecraft server logs for the selected server
func (m TuiModel) RenderMinecraftLogs(width, height int) []string {
	if height <= 0 {
		return nil
	}
	lines := make([]string, 0, height)

	serverName := m.SelectedServer()
	if serverName == "" {
		lines = append(lines, StyleHeader.Render(" MINECRAFT LOGS "))
		lines = append(lines, TrimToWidth(StyleSubtle.Render("  Select a server to view logs."), width))
		return PadLines(lines, height)
	}

	title := fmt.Sprintf(" LOGS: %s ", serverName)
	lines = append(lines, StyleHeader.Render(title))

	if !m.ConfigReady {
		lines = append(lines, TrimToWidth(StyleSubtle.Render("  API not connected."), width))
		return PadLines(lines, height)
	}

	if len(m.Logs) == 0 {
		lines = append(lines, TrimToWidth(StyleSubtle.Render("  Waiting for logs..."), width))
		return PadLines(lines, height)
	}

	start := 0
	if len(m.Logs) > height-1 {
		start = len(m.Logs) - (height - 1)
	}
	for _, line := range m.Logs[start:] {
		// Sanitize log line to remove ANSI codes that cause rendering issues on Linux
		sanitized := SanitizeLogLine(line)
		// 4 space indent to prevent overlap with nav menu
		lines = append(lines, TrimToWidth("    "+sanitized, width))
	}

	return PadLines(lines, height)
}

func FormatStatus(status string) string {
	value := strings.ToLower(strings.TrimSpace(status))
	switch value {
	case "running":
		return StyleRunning.Render(status)
	case "stopped", "exited":
		return StyleStopped.Render(status)
	default:
		return StyleSubtle.Render(status)
	}
}

// RenderServerActionsMain renders the server actions view
func (m TuiModel) RenderServerActionsMain(width, height int) []string {
	lines := make([]string, 0, height)

	serverName := m.SelectedServer()
	title := fmt.Sprintf(" SERVER: %s ", serverName)
	lines = append(lines, StyleHeader.Render(title))
	lines = append(lines, StyleSubtle.Render(strings.Repeat("─", width)))
	lines = append(lines, "")

	// Show server status
	if m.Selected >= 0 && m.Selected < len(m.Servers) {
		server := m.Servers[m.Selected]
		statusLine := "  Status: " + FormatStatus(server.Status)
		lines = append(lines, statusLine)
		lines = append(lines, "")
	}

	// Show actions
	lines = append(lines, StyleHeader.Render(" ACTIONS "))
	lines = append(lines, "")

	actions := GetServerActions()
	for i, action := range actions {
		prefix := "  "
		label := action.Label
		if action.Destructive {
			label = label + " !"
		}
		if i == m.ActionIndex {
			prefix = StyleSelected.Render("▶ ")
			label = StyleSelected.Render(label)
		}
		lines = append(lines, prefix+label)
	}

	lines = append(lines, "")
	lines = append(lines, StyleSubtle.Render("  [Enter] Select  [Esc] Back"))

	// Fill remaining space with logs
	usedHeight := len(lines) + 2 // +2 for separator and some padding
	logHeight := height - usedHeight
	if logHeight > 3 {
		lines = append(lines, "")
		lines = append(lines, StyleSubtle.Render(strings.Repeat("─", width)))
		logLines := m.RenderMinecraftLogs(width, logHeight)
		lines = append(lines, logLines...)
	}

	return PadLines(lines, height)
}
