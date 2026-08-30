package tui

import "github.com/charmbracelet/lipgloss"

var (
	StyleHeader   = lipgloss.NewStyle().Foreground(lipgloss.Color("39")).Bold(true)
	StyleSubtle   = lipgloss.NewStyle().Foreground(lipgloss.Color("246"))
	StyleSelected = lipgloss.NewStyle().Foreground(lipgloss.Color("205")).Bold(true)
	StyleRunning  = lipgloss.NewStyle().Foreground(lipgloss.Color("70"))
	StyleStopped  = lipgloss.NewStyle().Foreground(lipgloss.Color("214"))
	StyleError    = lipgloss.NewStyle().Foreground(lipgloss.Color("196")).Bold(true)
	StyleStatus   = lipgloss.NewStyle().Foreground(lipgloss.Color("81"))
)
