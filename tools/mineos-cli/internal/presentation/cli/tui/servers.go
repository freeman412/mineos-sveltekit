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
	header := fmt.Sprintf("  %-20s %-10s %-8s %-7s", "SERVER NAME", "STATUS", "PLAYERS", "MEM")
	lines = append(lines, StyleHeader.Render(header))
	lines = append(lines, StyleSubtle.Render(strings.Repeat("─", width)))

	if m.ErrMsg != "" {
		lines = append(lines, TrimToWidth(StyleError.Render(" Error: "+m.ErrMsg), width))
	}

	if len(m.Servers) == 0 {
		// Distinguish loading / unreachable / genuinely empty.
		var state string
		switch {
		case m.ContainersStopped:
			state = StyleSubtle.Render(" Containers are stopped.")
		case !m.ConfigReady:
			state = m.Spinner.View() + StyleSubtle.Render(" Connecting to API...")
		case m.ErrMsg != "":
			state = StyleSubtle.Render(" Server list unavailable.")
		case !m.ServersLoadedOnce:
			state = m.Spinner.View() + StyleSubtle.Render(" Loading servers...")
		default:
			state = StyleSubtle.Render(" No servers found.")
		}
		lines = append(lines, TrimToWidth(" "+state, width))
		return PadLines(lines, height)
	}

	for i, server := range m.Servers {
		prefix := "  "
		nameStyle := StyleHeader // Default
		if i == m.Selected {
			prefix = StyleSelected.Render("▶ ")
			nameStyle = StyleSelected
		}

		// Pad plain text before styling so column widths stay honest.
		name := nameStyle.Render(fmt.Sprintf("%-20s", server.Name))
		status := FormatStatus(fmt.Sprintf("%-10s", server.Status))
		players := fmt.Sprintf("%-8s", formatPlayers(server.PlayersOnline, server.PlayersMax))
		mem := fmt.Sprintf("%-7s", formatMemory(server.MemoryBytes))
		restart := ""
		if server.NeedsRestart {
			restart = " " + StyleError.Render("⟳ restart")
		}

		line := fmt.Sprintf("%s%s %s %s %s%s", prefix, name, status, players, mem, restart)
		lines = append(lines, TrimToWidth(line, width))
	}

	return PadLines(lines, height)
}

// formatPlayers renders "online/max" (or "online", or "—" when unknown).
func formatPlayers(online, max *int) string {
	if online == nil {
		return "—"
	}
	if max == nil {
		return fmt.Sprintf("%d", *online)
	}
	return fmt.Sprintf("%d/%d", *online, *max)
}

// formatMemory renders a byte count as a compact MiB/GiB value ("—" when unknown).
func formatMemory(b *int64) string {
	if b == nil || *b <= 0 {
		return "—"
	}
	mib := float64(*b) / (1024 * 1024)
	if mib >= 1024 {
		return fmt.Sprintf("%.1fG", mib/1024)
	}
	return fmt.Sprintf("%.0fM", mib)
}

// renderMetricsLines renders the live per-server metrics panel (streamed via SSE).
func (m TuiModel) renderMetricsLines() []string {
	lines := []string{StyleHeader.Render(" LIVE METRICS ")}
	p := m.PerfSample
	if p == nil {
		return append(lines, StyleSubtle.Render("  waiting for data…"), "")
	}
	tps := "—"
	tpsStyle := StyleRunning
	if p.Tps != nil {
		tps = fmt.Sprintf("%.1f", *p.Tps)
		if *p.Tps < 18 { // low-TPS highlight (matches the server-side alert threshold)
			tpsStyle = StyleError
		}
	}
	lines = append(lines, fmt.Sprintf("  TPS: %s   CPU: %.0f%%   RAM: %d/%d MB   Players: %d",
		tpsStyle.Render(tps), p.CpuPercent, p.RamUsedMb, p.RamTotalMb, p.PlayerCount))
	lines = append(lines, m.renderSparklines()...)
	return append(lines, "")
}

// renderSparklines renders TPS and CPU history strips from the sample buffer
// (history-endpoint backfill + live samples).
func (m TuiModel) renderSparklines() []string {
	if len(m.PerfHistory) < 2 {
		return nil
	}
	var tpsSeries, cpuSeries []float64
	for _, s := range m.PerfHistory {
		if s.Tps != nil {
			tpsSeries = append(tpsSeries, *s.Tps)
		}
		cpuSeries = append(cpuSeries, s.CpuPercent)
	}

	var lines []string
	if len(tpsSeries) >= 2 {
		lo, avg, hi := SeriesStats(tpsSeries)
		lines = append(lines, fmt.Sprintf("  TPS %s %s",
			StyleStatus.Render(Sparkline(tpsSeries, SparklineWidth)),
			StyleSubtle.Render(fmt.Sprintf("min %.1f  avg %.1f  max %.1f", lo, avg, hi))))
	}
	if len(cpuSeries) >= 2 {
		lo, avg, hi := SeriesStats(cpuSeries)
		lines = append(lines, fmt.Sprintf("  CPU %s %s",
			StyleStatus.Render(Sparkline(cpuSeries, SparklineWidth)),
			StyleSubtle.Render(fmt.Sprintf("min %.0f%%  avg %.0f%%  max %.0f%%  (last %dm)", lo, avg, hi, PerfHistoryMinutes))))
	}
	return lines
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

	// Live metrics panel (streamed via SSE while this view is open)
	lines = append(lines, m.renderMetricsLines()...)

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
