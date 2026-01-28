package tui

import (
	"fmt"
	"strings"

	"github.com/charmbracelet/lipgloss"
)

func (m TuiModel) View() string {
	if m.Width == 0 || m.Height == 0 {
		return "Loading..."
	}

	// 1. Render Header
	header := m.RenderHeader()
	headerHeight := lipgloss.Height(header)

	// 2. Define Layout Dimensions
	contentHeight := m.Height - headerHeight - 2
	if contentHeight < MinContentHeight {
		contentHeight = MinContentHeight
	}

	leftWidth := SidebarWidth
	rightWidth := m.Width - leftWidth - 1

	// 3. Render Navigation and Content
	leftLines := m.RenderNavSidebar(leftWidth, contentHeight)
	var rightLines []string

	switch m.CurrentView {
	case ViewDashboard:
		rightLines = m.RenderDashboardMain(rightWidth, contentHeight)
	case ViewServers:
		rightLines = m.RenderServersMain(rightWidth, contentHeight)
	case ViewServiceLogs:
		rightLines = m.RenderServiceLogsMain(rightWidth, contentHeight)
	case ViewSettings:
		rightLines = m.RenderSettingsMain(rightWidth, contentHeight)
	case ViewOutput:
		rightLines = m.RenderOutputMain(rightWidth, contentHeight)
	default:
		rightLines = []string{"View not implemented"}
	}

	// Overlay confirm dialog if active
	if m.Mode == ModeConfirm {
		rightLines = m.RenderConfirmDialog(rightWidth, contentHeight)
	}

	// 4. Assemble View
	var b strings.Builder
	b.WriteString(header)
	b.WriteString("\n")

	for i := 0; i < contentHeight; i++ {
		left := ""
		if i < len(leftLines) {
			left = leftLines[i]
		}
		right := ""
		if i < len(rightLines) {
			right = rightLines[i]
		}
		b.WriteString(PadRight(left, leftWidth))
		b.WriteString(StyleSubtle.Render("│"))
		b.WriteString(PadRight(right, rightWidth))
		b.WriteString("\n")
	}

	b.WriteString(m.RenderFooter())
	return b.String()
}

// RenderNavSidebar renders the unified navigation menu
func (m TuiModel) RenderNavSidebar(width, height int) []string {
	lines := make([]string, 0, height)

	// Check if scrolling is needed
	needsScrolling := len(m.NavItems) > height
	hasItemsAbove := m.NavScroll > 0
	hasItemsBelow := false

	// Reserve lines for scroll indicators
	reservedLines := 0
	if needsScrolling {
		reservedLines = 1 // Bottom indicator always shows position
		if hasItemsAbove {
			reservedLines++ // Top indicator
		}
	}

	visibleItems := height - reservedLines
	if visibleItems < 3 {
		visibleItems = 3
	}

	startIdx := m.NavScroll
	endIdx := startIdx + visibleItems
	if endIdx > len(m.NavItems) {
		endIdx = len(m.NavItems)
	}
	hasItemsBelow = endIdx < len(m.NavItems)

	// Show "more above" indicator
	if hasItemsAbove {
		lines = append(lines, StyleSubtle.Render(" ↑ more above"))
	}

	for i := startIdx; i < endIdx; i++ {
		item := m.NavItems[i]
		line := m.renderNavItem(item, i == m.NavIndex, width)
		lines = append(lines, line)
	}

	// Show scroll position/indicator at bottom
	if needsScrolling {
		if hasItemsBelow {
			scrollInfo := fmt.Sprintf(" ↓ [%d/%d]", m.NavIndex+1, len(m.NavItems))
			lines = append(lines, StyleSubtle.Render(scrollInfo))
		} else {
			scrollInfo := fmt.Sprintf(" [%d/%d]", m.NavIndex+1, len(m.NavItems))
			lines = append(lines, StyleSubtle.Render(scrollInfo))
		}
	}

	return PadLines(lines, height)
}

func (m TuiModel) renderNavItem(item NavItem, selected bool, width int) string {
	switch item.ItemType {
	case NavHeader:
		return StyleHeader.Render(" " + item.Label + " ")
	case NavSeparator:
		return StyleSubtle.Render(strings.Repeat("─", width-1))
	case NavView, NavAction:
		prefix := "  "
		label := item.Label
		if item.Destructive {
			label = label + " !"
		}
		if selected {
			prefix = StyleSelected.Render("▶ ")
			label = StyleSelected.Render(label)
		}
		return TrimToWidth(prefix+label, width)
	default:
		return ""
	}
}

// RenderOutputMain renders command output in the main content area
func (m TuiModel) RenderOutputMain(width, height int) []string {
	lines := make([]string, 0, height)

	title := " OUTPUT "
	if m.OutputTitle != "" {
		title = " " + m.OutputTitle + " "
	}
	lines = append(lines, StyleHeader.Render(title))
	lines = append(lines, StyleSubtle.Render(strings.Repeat("─", width)))
	lines = append(lines, "")

	// Reserve space for input if in interactive mode
	inputHeight := 0
	if m.Mode == ModeInteractive {
		inputHeight = 3
	}

	if len(m.OutputLines) == 0 {
		if m.StreamingRunning {
			lines = append(lines, StyleStatus.Render("  Streaming output..."))
			lines = append(lines, "")
			lines = append(lines, StyleSubtle.Render("  Waiting for command output..."))
		} else if m.InteractiveRunning {
			lines = append(lines, StyleSubtle.Render("  No output yet."))
		} else {
			lines = append(lines, StyleSubtle.Render("  No output."))
			lines = append(lines, "")
			lines = append(lines, StyleSubtle.Render("  Press Esc to go back."))
		}
	} else {
		// Show last N lines that fit
		available := height - 4 - inputHeight // Header + separator + blank + input
		start := 0
		if len(m.OutputLines) > available {
			start = len(m.OutputLines) - available
		}
		for _, line := range m.OutputLines[start:] {
			lines = append(lines, TrimToWidth("  "+line, width))
		}
	}

	// Show input field in interactive mode
	if m.Mode == ModeInteractive {
		// Pad to push input to bottom
		for len(lines) < height-inputHeight {
			lines = append(lines, "")
		}
		lines = append(lines, StyleSubtle.Render(strings.Repeat("─", width)))
		lines = append(lines, StyleStatus.Render("  INPUT: ")+m.Input.View())
		lines = append(lines, StyleSubtle.Render("  [Enter] Send  [Ctrl+C] Cancel"))
	}

	return PadLines(lines, height)
}

// RenderConfirmDialog renders a confirmation dialog for destructive actions
func (m TuiModel) RenderConfirmDialog(width, height int) []string {
	lines := make([]string, 0, height)

	// Center the dialog vertically
	topPadding := (height - 10) / 2
	if topPadding < 0 {
		topPadding = 0
	}
	for i := 0; i < topPadding; i++ {
		lines = append(lines, "")
	}

	// Dialog box
	boxWidth := width - 4
	if boxWidth < 30 {
		boxWidth = 30
	}

	border := strings.Repeat("─", boxWidth)
	lines = append(lines, StyleError.Render("  ┌"+border+"┐"))

	title := " CONFIRM ACTION "
	titlePadded := centerText(title, boxWidth)
	lines = append(lines, StyleError.Render("  │")+StyleHeader.Render(titlePadded)+StyleError.Render("│"))

	lines = append(lines, StyleError.Render("  │"+strings.Repeat(" ", boxWidth)+"│"))

	// Action name
	if m.ConfirmAction != nil {
		actionLine := centerText(m.ConfirmAction.Label, boxWidth)
		lines = append(lines, StyleError.Render("  │")+StyleSelected.Render(actionLine)+StyleError.Render("│"))
	}

	lines = append(lines, StyleError.Render("  │"+strings.Repeat(" ", boxWidth)+"│"))

	// Message
	if m.ConfirmMessage != "" {
		msgLine := centerText(m.ConfirmMessage, boxWidth)
		lines = append(lines, StyleError.Render("  │")+msgLine+StyleError.Render("│"))
	}

	lines = append(lines, StyleError.Render("  │"+strings.Repeat(" ", boxWidth)+"│"))

	// Instructions
	instructions := "[Enter] Confirm  [Esc] Cancel"
	instructionsPadded := centerText(instructions, boxWidth)
	lines = append(lines, StyleError.Render("  │")+StyleSubtle.Render(instructionsPadded)+StyleError.Render("│"))

	lines = append(lines, StyleError.Render("  └"+border+"┘"))

	return PadLines(lines, height)
}

// centerText centers text within a given width
func centerText(text string, width int) string {
	if len(text) >= width {
		return text[:width]
	}
	padding := (width - len(text)) / 2
	return strings.Repeat(" ", padding) + text + strings.Repeat(" ", width-padding-len(text))
}
