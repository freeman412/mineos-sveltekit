package tui

import (
	"fmt"

	"github.com/charmbracelet/lipgloss"
)

func (m TuiModel) RenderHeader() string {
	// API Health
	health := StyleError.Render("● UNHEALTHY")
	if m.ContainersStopped {
		health = StyleSubtle.Render("● STOPPED")
	} else if m.ConfigReady && m.ErrMsg == "" {
		health = StyleRunning.Render("● HEALTHY")
	}

	// Logo/Title
	logo := StyleHeader.Render(" MineOS ")

	// Persistent Info (Top Left Box)
	infoLines := []string{
		fmt.Sprintf("API:       %s", health),
		fmt.Sprintf("Origin:    %s", StyleStatus.Render(Fallback(m.Cfg.WebOrigin, "http://localhost:3000"))),
		fmt.Sprintf("MC Host:   %s", StyleStatus.Render(Fallback(m.Cfg.MinecraftHost, "localhost"))),
		fmt.Sprintf("Docker:    %s", m.RenderDockerStatus()),
	}

	// Layout Header
	headerLeft := lipgloss.JoinVertical(lipgloss.Left, infoLines...)

	// Pad and join
	totalWidth := m.Width

	joined := lipgloss.NewStyle().Width(totalWidth).Padding(0, 1).Render(logo + "\n" + headerLeft)

	return lipgloss.NewStyle().
		Border(lipgloss.NormalBorder(), false, false, true, false).
		BorderForeground(lipgloss.Color("240")).
		Width(totalWidth).
		Render(joined)
}

func (m TuiModel) RenderDockerStatus() string {
	if !m.ComposeReady {
		return StyleError.Render("Unavailable")
	}
	return StyleRunning.Render("Ready")
}

func Fallback(value, fallbackValue string) string {
	if value == "" {
		return fallbackValue
	}
	return value
}
