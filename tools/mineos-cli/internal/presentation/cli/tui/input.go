package tui

import (
	"strings"

	"github.com/charmbracelet/bubbles/textinput"
	tea "github.com/charmbracelet/bubbletea"
)

// HandleKey processes key input in normal mode
func (m TuiModel) HandleKey(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	switch msg.Type {
	case tea.KeyCtrlC:
		m.Quitting = true
		m.StopLogs()
		return m, tea.Quit

	case tea.KeyUp:
		return m.navUp()

	case tea.KeyDown:
		return m.navDown()

	case tea.KeyLeft:
		return m.navLeft()

	case tea.KeyRight:
		return m.navRight()

	case tea.KeyEnter:
		return m.navSelect()

	case tea.KeyEsc:
		return m.navBack()
	}

	// Vim-style navigation
	switch msg.String() {
	case "q":
		m.Quitting = true
		m.StopLogs()
		return m, tea.Quit
	case "j":
		return m.navDown()
	case "k":
		return m.navUp()
	case "h":
		return m.navLeft()
	case "l":
		return m.navRight()
	}

	return m, nil
}

// navLeft handles left arrow - switches log source in service logs view
func (m TuiModel) navLeft() (tea.Model, tea.Cmd) {
	if m.CurrentView == ViewServiceLogs && len(m.ComposeServices) > 1 {
		// Find current index and go to previous
		sources := m.ComposeServices
		for i, svc := range sources {
			if svc == m.LogSource {
				prevIdx := i - 1
				if prevIdx < 0 {
					prevIdx = len(sources) - 1
				}
				m.LogSource = sources[prevIdx]
				m.Logs = nil
				return m, m.StartLogStreamCmd()
			}
		}
	}
	return m, nil
}

// navRight handles right arrow - switches log source in service logs view
func (m TuiModel) navRight() (tea.Model, tea.Cmd) {
	if m.CurrentView == ViewServiceLogs && len(m.ComposeServices) > 1 {
		// Find current index and go to next
		sources := m.ComposeServices
		for i, svc := range sources {
			if svc == m.LogSource {
				nextIdx := (i + 1) % len(sources)
				m.LogSource = sources[nextIdx]
				m.Logs = nil
				return m, m.StartLogStreamCmd()
			}
		}
	}
	return m, nil
}

// navUp moves selection up in the navigation menu
func (m TuiModel) navUp() (tea.Model, tea.Cmd) {
	// In servers view with server actions mode
	if m.CurrentView == ViewServers && m.ServerActions {
		if m.ActionIndex > 0 {
			m.ActionIndex--
		}
		return m, nil
	}

	// In servers view, handle server selection
	if m.CurrentView == ViewServers && len(m.Servers) > 0 && m.Selected > 0 {
		m.Selected--
		// Always show Minecraft logs for selected server
		m.MinecraftSource = m.SelectedServer()
		m.Logs = nil
		return m, m.StartLogStreamCmd()
	}

	// Navigate the menu
	newIndex := NextSelectableIndex(m.NavItems, m.NavIndex, -1)
	if newIndex != m.NavIndex {
		m.NavIndex = newIndex
		m.adjustScroll()
	}
	return m, nil
}

// navDown moves selection down in the navigation menu
func (m TuiModel) navDown() (tea.Model, tea.Cmd) {
	// In servers view with server actions mode
	if m.CurrentView == ViewServers && m.ServerActions {
		if m.ActionIndex < len(GetServerActions())-1 {
			m.ActionIndex++
		}
		return m, nil
	}

	// In servers view, handle server selection
	if m.CurrentView == ViewServers && len(m.Servers) > 0 && m.Selected < len(m.Servers)-1 {
		m.Selected++
		// Always show Minecraft logs for selected server
		m.MinecraftSource = m.SelectedServer()
		m.Logs = nil
		return m, m.StartLogStreamCmd()
	}

	// Navigate the menu
	newIndex := NextSelectableIndex(m.NavItems, m.NavIndex, 1)
	if newIndex != m.NavIndex {
		m.NavIndex = newIndex
		m.adjustScroll()
	}
	return m, nil
}

// adjustScroll adjusts the scroll offset to keep selection visible
func (m *TuiModel) adjustScroll() {
	visibleItems := m.Height - 10 // Approximate visible items
	if visibleItems < 5 {
		visibleItems = 5
	}

	if m.NavIndex < m.NavScroll {
		m.NavScroll = m.NavIndex
	} else if m.NavIndex >= m.NavScroll+visibleItems {
		m.NavScroll = m.NavIndex - visibleItems + 1
	}
}

// navSelect handles Enter key - activates the selected menu item
func (m TuiModel) navSelect() (tea.Model, tea.Cmd) {
	// Handle server actions mode
	if m.CurrentView == ViewServers && m.ServerActions {
		return m.executeServerAction()
	}

	// In servers view without server actions, Enter selects a server and shows actions
	if m.CurrentView == ViewServers && len(m.Servers) > 0 {
		m.ServerActions = true
		m.ActionIndex = 0
		return m, nil
	}

	if m.NavIndex < 0 || m.NavIndex >= len(m.NavItems) {
		return m, nil
	}

	item := m.NavItems[m.NavIndex]

	switch item.ItemType {
	case NavView:
		m.PreviousView = m.CurrentView
		m.CurrentView = item.View
		// Reset server actions mode when switching views
		m.ServerActions = false
		m.ActionIndex = 0

		// Switch log type based on view
		var cmd tea.Cmd
		if item.View == ViewServers {
			// Switch to Minecraft logs for selected server
			m.LogType = LogTypeMinecraft
			m.MinecraftSource = m.SelectedServer()
			m.Logs = nil
			cmd = m.StartLogStreamCmd()
		} else if item.View == ViewServiceLogs {
			// Switch to Docker logs
			m.LogType = LogTypeDocker
			m.Logs = nil
			cmd = m.StartLogStreamCmd()
		}
		return m, cmd

	case NavAction:
		if item.Action == nil {
			return m, nil
		}

		// Handle special actions
		if item.Action.Args[0] == "console" {
			if m.SelectedServer() == "" {
				m.ErrMsg = "Select a server first (go to Servers view)"
				return m, nil
			}
			m.Mode = ModeCommand
			m.Input.SetValue("")
			m.Input.Focus()
			return m, textinput.Blink
		}

		// Check if action requires confirmation
		if item.Destructive {
			m.ConfirmAction = item.Action
			m.ConfirmMessage = "This action may cause data loss. Continue?"
			m.Mode = ModeConfirm
			return m, nil
		}

		// Execute the action
		return m.executeNavAction(item)
	}

	return m, nil
}

// executeServerAction executes the selected server action
func (m TuiModel) executeServerAction() (tea.Model, tea.Cmd) {
	actions := GetServerActions()
	if m.ActionIndex < 0 || m.ActionIndex >= len(actions) {
		return m, nil
	}

	action := actions[m.ActionIndex]
	serverName := m.SelectedServer()

	// Handle back action
	if action.Action == "back" {
		m.ServerActions = false
		m.ActionIndex = 0
		return m, nil
	}

	// Handle console command
	if action.Action == "console" {
		m.Mode = ModeCommand
		m.Input.SetValue("")
		m.Input.Focus()
		return m, textinput.Blink
	}

	// Handle destructive actions
	if action.Destructive {
		menuItem := &MenuItem{
			Label:       action.Label,
			Args:        []string{"servers", serverName, action.Action},
			Destructive: true,
		}
		m.ConfirmAction = menuItem
		m.ConfirmMessage = "This action may cause data loss. Continue?"
		m.Mode = ModeConfirm
		return m, nil
	}

	// Execute the server action
	m.PreviousView = m.CurrentView
	m.CurrentView = ViewOutput
	m.OutputTitle = action.Label + ": " + serverName
	m.OutputLines = []string{"Executing " + action.Label + " on " + serverName + "..."}

	menuItem := MenuItem{
		Label: action.Label,
		Args:  []string{"servers", serverName, action.Action},
	}
	return m, m.ExecMenuItem(menuItem)
}

// navBack handles Esc key - goes back to previous view or exits
func (m TuiModel) navBack() (tea.Model, tea.Cmd) {
	// If in server actions mode, go back to server list
	if m.CurrentView == ViewServers && m.ServerActions {
		m.ServerActions = false
		m.ActionIndex = 0
		return m, nil
	}

	// If in output view, go back to previous view
	if m.CurrentView == ViewOutput {
		m.CurrentView = m.PreviousView
		return m, nil
	}

	// If we have a previous view, go back to it
	if m.PreviousView != m.CurrentView {
		m.CurrentView = m.PreviousView
		return m, nil
	}

	return m, nil
}

// executeNavAction executes a navigation action and shows output
func (m TuiModel) executeNavAction(item NavItem) (tea.Model, tea.Cmd) {
	if item.Action == nil {
		return m, nil
	}

	// Build the command args, substituting server name if needed
	args := make([]string, len(item.Action.Args))
	copy(args, item.Action.Args)

	// For server actions, substitute the selected server
	if len(args) >= 2 && args[0] == "servers" && args[1] == "" {
		server := m.SelectedServer()
		if server == "" {
			m.ErrMsg = "No server selected. Go to Servers view first."
			return m, nil
		}
		args[1] = server
	}

	// Switch to output view for all commands
	m.PreviousView = m.CurrentView
	m.CurrentView = ViewOutput
	m.OutputTitle = item.Label
	m.OutputLines = []string{"Executing " + item.Label + "..."}

	// Execute via CLI subprocess
	menuItem := MenuItem{
		Label:       item.Label,
		Args:        args,
		Interactive: item.Action.Interactive,
		Streaming:   item.Action.Streaming,
	}
	return m, m.ExecMenuItem(menuItem)
}

// HandleCommandInput handles input when in command mode
func (m TuiModel) HandleCommandInput(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	switch msg.Type {
	case tea.KeyEsc:
		m.Mode = ModeNormal
		m.Input.Blur()
		return m, nil
	case tea.KeyEnter:
		command := strings.TrimSpace(m.Input.Value())
		m.Mode = ModeNormal
		m.Input.Blur()
		if command == "" {
			return m, nil
		}
		return m, m.ConsoleCommandCmd(command)
	}

	var cmd tea.Cmd
	m.Input, cmd = m.Input.Update(msg)
	return m, cmd
}

// HandleInteractiveInput handles input when running an interactive command
func (m TuiModel) HandleInteractiveInput(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	switch msg.Type {
	case tea.KeyCtrlC:
		// Cancel the interactive command
		if m.InteractiveStdin != nil {
			m.InteractiveStdin.Close()
		}
		m.Mode = ModeNormal
		m.InteractiveRunning = false
		m.InteractiveStdin = nil
		m.InteractiveOutput = nil
		m.Input.Blur()
		m.OutputLines = append(m.OutputLines, "", "Cancelled.")
		return m, nil

	case tea.KeyEnter:
		// Send input to the command
		input := m.Input.Value()
		m.Input.SetValue("")
		m.OutputLines = append(m.OutputLines, "> "+input)
		return m, m.SendInteractiveInput(input)
	}

	// Update the text input
	var cmd tea.Cmd
	m.Input, cmd = m.Input.Update(msg)
	return m, cmd
}

// HandleConfirmInput handles input when in confirm mode
func (m TuiModel) HandleConfirmInput(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	switch msg.Type {
	case tea.KeyEsc:
		m.Mode = ModeNormal
		m.ConfirmAction = nil
		m.ConfirmMessage = ""
		return m, nil

	case tea.KeyEnter:
		if m.ConfirmAction != nil {
			action := m.ConfirmAction
			m.Mode = ModeNormal
			m.ConfirmAction = nil
			m.ConfirmMessage = ""

			// Switch to output view for all commands
			m.PreviousView = m.CurrentView
			m.CurrentView = ViewOutput
			m.OutputTitle = action.Label
			m.OutputLines = []string{"Executing " + action.Label + "..."}

			return m, m.ExecMenuItem(*action)
		}
		m.Mode = ModeNormal
		return m, nil
	}

	switch msg.String() {
	case "y", "Y":
		if m.ConfirmAction != nil {
			action := m.ConfirmAction
			m.Mode = ModeNormal
			m.ConfirmAction = nil
			m.ConfirmMessage = ""

			// Switch to output view for all commands
			m.PreviousView = m.CurrentView
			m.CurrentView = ViewOutput
			m.OutputTitle = action.Label
			m.OutputLines = []string{"Executing " + action.Label + "..."}

			return m, m.ExecMenuItem(*action)
		}
		return m, nil

	case "n", "N":
		m.Mode = ModeNormal
		m.ConfirmAction = nil
		m.ConfirmMessage = ""
		return m, nil
	}

	return m, nil
}

// NextLogSource cycles to the next log source
func (m TuiModel) NextLogSource(current string, services []string) string {
	if len(services) == 0 {
		return DefaultDockerLogSource
	}
	sources := append([]string{DefaultDockerLogSource}, services...)
	for i, s := range sources {
		if s == current {
			return sources[(i+1)%len(sources)]
		}
	}
	return sources[0]
}

// NextMinecraftLogType cycles to the next minecraft log type
func (m TuiModel) NextMinecraftLogType(current string) string {
	for i, t := range MinecraftLogTypes {
		if t == current {
			return MinecraftLogTypes[(i+1)%len(MinecraftLogTypes)]
		}
	}
	return "combined"
}
