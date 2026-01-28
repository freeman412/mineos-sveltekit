package tui

import (
	"fmt"
	"strings"
)

func (m TuiModel) RenderDashboardMain(width, height int) []string {
	lines := make([]string, 0, height)

	// Banner
	for _, bannerLine := range strings.Split(Banner, "\n") {
		lines = append(lines, StyleHeader.Render(bannerLine))
	}
	lines = append(lines, StyleSubtle.Render(BannerTagline))
	lines = append(lines, "")
	lines = append(lines, StyleSubtle.Render(strings.Repeat("─", width)))
	lines = append(lines, "")

	// API Status - based on config ready and server list loaded
	lines = append(lines, StyleHeader.Render("API Status"))
	var health string
	if m.ContainersStopped {
		health = StyleSubtle.Render("stopped (containers down)")
	} else if m.ConfigReady && m.Client != nil && m.ErrMsg == "" {
		health = StyleRunning.Render("connected")
	} else if !m.ConfigReady && m.ErrMsg == "" {
		health = StyleSubtle.Render("loading...")
	} else {
		health = StyleError.Render("not connected")
	}
	lines = append(lines, "  "+health)
	// Only show error message if not connected and not intentionally stopped
	if m.ErrMsg != "" && !m.ContainersStopped {
		// Truncate long error messages
		errDisplay := m.ErrMsg
		if len(errDisplay) > width-4 {
			errDisplay = errDisplay[:width-7] + "..."
		}
		lines = append(lines, "  "+StyleError.Render(errDisplay))
	}
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
