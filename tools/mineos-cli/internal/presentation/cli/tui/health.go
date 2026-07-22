package tui

import (
	"fmt"
	"sort"
	"strings"
	"time"
)

// MaxHealthRows caps each health-view section so one busy section can't push
// the others off-screen.
const MaxHealthRows = 8

func (m TuiModel) RenderHealthMain(width, height int) []string {
	lines := make([]string, 0, height)
	lines = append(lines, StyleHeader.Render(" HEALTH & ALERTS "))
	lines = append(lines, StyleSubtle.Render(strings.Repeat("─", width)))

	if !m.ConfigReady {
		lines = append(lines, TrimToWidth(StyleSubtle.Render("  API not connected."), width))
		return PadLines(lines, height)
	}
	if m.HealthDataErr != "" {
		lines = append(lines, TrimToWidth(StyleError.Render("  Error: "+m.HealthDataErr), width))
		return PadLines(lines, height)
	}
	if !m.HealthDataLoaded {
		lines = append(lines, TrimToWidth(StyleSubtle.Render("  Loading health data..."), width))
		return PadLines(lines, height)
	}

	now := time.Now()
	lines = append(lines, "")
	lines = append(lines, m.renderWatchdogSection(width, now)...)
	lines = append(lines, "")
	lines = append(lines, m.renderCrashesSection(width, now)...)
	lines = append(lines, "")
	lines = append(lines, m.renderAlertsSection(width, now)...)

	return PadLines(lines, height)
}

func (m TuiModel) renderWatchdogSection(width int, now time.Time) []string {
	lines := []string{StyleHeader.Render(" WATCHDOG ")}
	if len(m.Watchdog) == 0 {
		return append(lines, TrimToWidth(StyleSubtle.Render("  No servers monitored."), width))
	}

	names := make([]string, 0, len(m.Watchdog))
	for name := range m.Watchdog {
		names = append(names, name)
	}
	sort.Strings(names)

	for i, name := range names {
		if i >= MaxHealthRows {
			lines = append(lines, TrimToWidth(StyleSubtle.Render(fmt.Sprintf("  … %d more", len(names)-MaxHealthRows)), width))
			break
		}
		s := m.Watchdog[name]
		state := StyleSubtle.Render("idle")
		if s.CooldownEndsAt != nil && s.CooldownEndsAt.After(now) {
			state = StyleStopped.Render("cooldown " + formatDuration(s.CooldownEndsAt.Sub(now)))
		} else if s.IsMonitoring {
			state = StyleRunning.Render("monitoring")
		}
		detail := ""
		if s.RestartAttempts > 0 {
			detail += fmt.Sprintf("  restarts: %d", s.RestartAttempts)
		}
		if s.LastCrashTime != nil {
			detail += "  last crash: " + timeAgo(*s.LastCrashTime, now)
		}
		line := fmt.Sprintf("  %-20s %s%s", name, state, StyleSubtle.Render(detail))
		lines = append(lines, TrimToWidth(line, width))
	}
	return lines
}

func (m TuiModel) renderCrashesSection(width int, now time.Time) []string {
	lines := []string{StyleHeader.Render(" RECENT CRASHES ")}
	if len(m.Crashes) == 0 {
		return append(lines, TrimToWidth(StyleSubtle.Render("  No crashes recorded."), width))
	}
	for i, c := range m.Crashes {
		if i >= MaxHealthRows {
			lines = append(lines, TrimToWidth(StyleSubtle.Render(fmt.Sprintf("  … %d more", len(m.Crashes)-MaxHealthRows)), width))
			break
		}
		restart := StyleSubtle.Render("no auto-restart")
		if c.AutoRestartAttempted {
			if c.AutoRestartSucceeded {
				restart = StyleRunning.Render("auto-restart ✓")
			} else {
				restart = StyleError.Render("auto-restart ✗")
			}
		}
		line := fmt.Sprintf("  %-9s %-20s %-13s %s",
			timeAgo(c.DetectedAt, now), c.ServerName, c.CrashType, restart)
		lines = append(lines, TrimToWidth(line, width))
	}
	return lines
}

func (m TuiModel) renderAlertsSection(width int, now time.Time) []string {
	unread := 0
	for _, a := range m.Alerts {
		if !a.IsRead {
			unread++
		}
	}
	title := fmt.Sprintf(" ALERTS (%d unread) ", unread)
	lines := []string{StyleHeader.Render(title)}
	if len(m.Alerts) == 0 {
		return append(lines, TrimToWidth(StyleSubtle.Render("  No active alerts."), width))
	}
	for i, a := range m.Alerts {
		if i >= MaxHealthRows {
			lines = append(lines, TrimToWidth(StyleSubtle.Render(fmt.Sprintf("  … %d more", len(m.Alerts)-MaxHealthRows)), width))
			break
		}
		badge := formatAlertType(a.Type)
		scope := ""
		if a.ServerName != nil && *a.ServerName != "" {
			scope = " — " + *a.ServerName
		}
		marker := "  "
		if !a.IsRead {
			marker = StyleSelected.Render("• ")
		}
		line := fmt.Sprintf("  %s%s %s%s %s",
			marker, badge, a.Title, scope, StyleSubtle.Render("("+timeAgo(a.CreatedAt, now)+")"))
		lines = append(lines, TrimToWidth(line, width))
	}
	return lines
}

func formatAlertType(t string) string {
	switch strings.ToLower(t) {
	case "error":
		return StyleError.Render("[error]")
	case "warning":
		return StyleStopped.Render("[warn] ")
	case "success":
		return StyleRunning.Render("[ok]   ")
	default:
		return StyleSubtle.Render("[info] ")
	}
}

// timeAgo renders a compact relative timestamp ("5m ago", "2h ago").
func timeAgo(t time.Time, now time.Time) string {
	d := now.Sub(t)
	if d < 0 {
		d = 0
	}
	return formatDuration(d) + " ago"
}

// formatDuration renders a duration compactly at a single unit ("45s", "5m", "2h", "3d").
func formatDuration(d time.Duration) string {
	switch {
	case d < time.Minute:
		return fmt.Sprintf("%ds", int(d.Seconds()))
	case d < time.Hour:
		return fmt.Sprintf("%dm", int(d.Minutes()))
	case d < 24*time.Hour:
		return fmt.Sprintf("%dh", int(d.Hours()))
	default:
		return fmt.Sprintf("%dd", int(d.Hours()/24))
	}
}
