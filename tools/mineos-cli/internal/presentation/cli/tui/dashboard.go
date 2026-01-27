package tui

import (
	"fmt"
	"strings"
)

func (m TuiModel) RenderDashboardMain(width, height int) []string {
	lines := make([]string, 0, height)

	lines = append(lines, StyleHeader.Render(" DASHBOARD "))
	lines = append(lines, StyleSubtle.Render(strings.Repeat("─", width)))
	lines = append(lines, "")

	// API Status
	lines = append(lines, StyleHeader.Render("API Status"))
	health := StyleError.Render("unhealthy")
	if m.ConfigReady && m.ErrMsg == "" {
		health = StyleRunning.Render("healthy")
	} else if m.ErrMsg != "" {
		health = StyleError.Render("error")
	}
	lines = append(lines, "  "+health)
	lines = append(lines, "")

	// Stack Health
	lines = append(lines, StyleHeader.Render("Stack Health"))
	composeStatus := StyleSubtle.Render("not ready")
	if m.ComposeReady {
		composeStatus = StyleRunning.Render("ready")
	}
	lines = append(lines, "  Compose: "+composeStatus)
	if len(m.ComposeServices) > 0 {
		lines = append(lines, fmt.Sprintf("  Services: %d", len(m.ComposeServices)))
	}
	if m.ComposeError != "" {
		lines = append(lines, "  Error: "+m.ComposeError)
	}
	lines = append(lines, "")

	// Configuration
	if m.ConfigReady {
		lines = append(lines, StyleHeader.Render("Configuration"))
		lines = append(lines, "  Web Origin: "+Fallback(m.Cfg.WebOrigin, "http://localhost:3000"))
		lines = append(lines, "  MC Host:    "+Fallback(m.Cfg.MinecraftHost, "localhost"))
		lines = append(lines, "  Port:       "+Fallback(m.Cfg.ApiPort, "5078"))
	}

	if m.StatusMsg != "" {
		lines = append(lines, "")
		lines = append(lines, StyleStatus.Render("Last action: "+m.StatusMsg))
	}

	return PadLines(lines, height)
}
