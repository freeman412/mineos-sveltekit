package tui

import (
	"strings"

	"github.com/charmbracelet/lipgloss"
	"github.com/charmbracelet/x/ansi"
)

func PadRight(text string, width int) string {
	if width <= 0 {
		return ""
	}
	visible := lipgloss.Width(text)
	if visible >= width {
		return ansi.Truncate(text, width, "")
	}
	return text + strings.Repeat(" ", width-visible)
}

func TrimToWidth(text string, width int) string {
	if width <= 0 {
		return ""
	}
	return ansi.Truncate(text, width, "")
}

func PadLines(lines []string, height int) []string {
	for len(lines) < height {
		lines = append(lines, "")
	}
	return lines
}

func Mask(value string) string {
	if value == "" {
		return "(empty)"
	}
	if len(value) <= 6 {
		return "***"
	}
	return value[:3] + "***" + value[len(value)-3:]
}
