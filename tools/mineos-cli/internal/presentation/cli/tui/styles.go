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
	StyleServiceA = lipgloss.NewStyle().Foreground(lipgloss.Color("75"))
	StyleServiceB = lipgloss.NewStyle().Foreground(lipgloss.Color("135"))
	StyleServiceC = lipgloss.NewStyle().Foreground(lipgloss.Color("112"))
	ServiceStyles = []lipgloss.Style{StyleServiceA, StyleServiceB, StyleServiceC, StyleRunning, StyleStatus}
)
