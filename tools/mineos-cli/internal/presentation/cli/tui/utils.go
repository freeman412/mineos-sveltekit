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

// SanitizeLogLine removes ANSI escape codes and control characters from log lines
// to prevent rendering issues on Linux/bash terminals where they can cause cursor
// movement and text overwrites
func SanitizeLogLine(line string) string {
	// Strip ANSI escape codes (colors, cursor movements, etc)
	stripped := ansi.Strip(line)

	// Remove carriage returns to prevent line overwrites
	stripped = strings.ReplaceAll(stripped, "\r", "")

	// Prevent embedded newlines/tabs from breaking layout; normalize to spaces.
	stripped = strings.ReplaceAll(stripped, "\n", " ")
	stripped = strings.ReplaceAll(stripped, "\t", "  ")

	// Remove any remaining control characters (ASCII < 32, plus DEL)
	var result strings.Builder
	for _, r := range stripped {
		if r >= 32 && r != 127 {
			result.WriteRune(r)
		}
	}

	return result.String()
}
