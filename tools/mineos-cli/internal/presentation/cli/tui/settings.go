package tui

import (
	"strings"
)

func (m TuiModel) RenderSettingsMain(width, height int) []string {
	lines := make([]string, 0, height)

	lines = append(lines, StyleHeader.Render(" SETTINGS "))
	lines = append(lines, StyleSubtle.Render(strings.Repeat("─", width)))
	lines = append(lines, "")

	if !m.ConfigReady {
		lines = append(lines, StyleError.Render("Configuration not loaded."))
		return PadLines(lines, height)
	}

	lines = append(lines, StyleHeader.Render("API Configuration"))
	lines = append(lines, "  Key:     "+Mask(m.Cfg.ManagementApiKey))
	lines = append(lines, "  Static:  "+Mask(m.Cfg.ApiKeyStatic))
	lines = append(lines, "  Seed:    "+Mask(m.Cfg.ApiKeySeed))
	lines = append(lines, "")

	lines = append(lines, StyleHeader.Render("Environment"))
	lines = append(lines, "  Path:    "+m.Cfg.EnvPath)
	lines = append(lines, "  Port:    "+Fallback(m.Cfg.ApiPort, "5078"))
	lines = append(lines, "  Host:    "+Fallback(m.Cfg.MinecraftHost, "localhost"))
	stackChannel := StyleRunning.Render("Stable (latest)")
	tag := strings.TrimSpace(m.Cfg.ImageTag)
	if tag == "" || tag == "latest" {
		// stable
	} else if tag == "preview" {
		stackChannel = StyleError.Render("Preview")
	} else {
		stackChannel = StyleStatus.Render("Pinned (" + tag + ")")
	}
	lines = append(lines, "  Stack:   "+stackChannel)
	lines = append(lines, "")

	lines = append(lines, StyleHeader.Render("Database"))
	lines = append(lines, "  Type:    "+Fallback(m.Cfg.DatabaseType, "sqlite"))
	lines = append(lines, "  Conn:    "+Mask(m.Cfg.DatabaseConnection))
	lines = append(lines, "")

	// Update channel toggle
	lines = append(lines, StyleHeader.Render("Updates"))
	channel := StyleRunning.Render("Stable")
	if m.Cfg.IsPreReleaseEnabled() {
		channel = StyleError.Render("Preview (Pre-release)")
	}
	lines = append(lines, "  Channel: "+channel+"  "+StyleSubtle.Render("[p] toggle"))
	if m.Cfg.IsPreReleaseEnabled() {
		lines = append(lines, "  "+StyleError.Render("Warning: Preview builds may be unstable or cause data loss."))
	}
	lines = append(lines, "")

	lines = append(lines, StyleSubtle.Render("Use SYSTEM menu to reconfigure or update."))

	return PadLines(lines, height)
}
