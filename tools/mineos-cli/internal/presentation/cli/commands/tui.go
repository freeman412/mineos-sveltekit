package commands

import (
	"context"
	"errors"
	"fmt"
	"sort"
	"strings"

	"github.com/charmbracelet/bubbles/textinput"
	tea "github.com/charmbracelet/bubbletea"
	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/ports"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

type tuiMode int

const (
	tuiModeNormal tuiMode = iota
	tuiModeCommand
)

type tuiModel struct {
	loadConfig *usecases.LoadConfigUseCase

	width  int
	height int

	client *api.Client
	cfg    config.Config

	servers  []ports.Server
	selected int

	logs        []string
	logsActive  bool
	logSource   string
	logsChan    <-chan api.LogEntry
	logErrsChan <-chan error
	logCancel   context.CancelFunc

	statusMsg string
	errMsg    string

	mode     tuiMode
	input    textinput.Model
	quitting bool
}

type (
	configLoadedMsg struct {
		cfg config.Config
		err error
	}
	serversLoadedMsg struct {
		servers []ports.Server
		err     error
	}
	logEntryMsg struct {
		entry api.LogEntry
	}
	logErrorMsg struct {
		err error
	}
	actionResultMsg struct {
		message string
		err     error
	}
)

var logSources = []string{"combined", "server", "java", "crash"}

func NewTuiCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:     "tui",
		Aliases: []string{"ui"},
		Short:   "Full-screen MineOS dashboard",
		RunE: func(cmd *cobra.Command, _ []string) error {
			model := newTuiModel(loadConfig)
			program := tea.NewProgram(model, tea.WithAltScreen())
			_, err := program.Run()
			return err
		},
	}
}

func newTuiModel(loadConfig *usecases.LoadConfigUseCase) tuiModel {
	input := textinput.New()
	input.Placeholder = "console command"
	input.CharLimit = 2048
	input.Width = 50

	return tuiModel{
		loadConfig: loadConfig,
		logsActive: true,
		logSource:  "combined",
		mode:       tuiModeNormal,
		input:      input,
	}
}

func (m tuiModel) Init() tea.Cmd {
	return loadConfigCmd(m.loadConfig)
}

func (m tuiModel) Update(msg tea.Msg) (tea.Model, tea.Cmd) {
	switch msg := msg.(type) {
	case tea.WindowSizeMsg:
		m.width = msg.Width
		m.height = msg.Height
		return m, nil
	case configLoadedMsg:
		if msg.err != nil {
			m.errMsg = msg.err.Error()
			return m, nil
		}
		m.cfg = msg.cfg
		m.client = api.NewClientFromConfig(msg.cfg)
		return m, loadServersCmd(m.client)
	case serversLoadedMsg:
		if msg.err != nil {
			m.errMsg = msg.err.Error()
			return m, nil
		}
		m.servers = msg.servers
		if len(m.servers) == 0 {
			m.selected = 0
			m.logs = nil
			m.stopLogs()
			return m, nil
		}
		if m.selected >= len(m.servers) {
			m.selected = len(m.servers) - 1
		}
		return m, m.ensureLogStream()
	case logEntryMsg:
		m.appendLog(formatLogEntry(msg.entry))
		return m, listenLogsCmd(m.logsChan, m.logErrsChan)
	case logErrorMsg:
		if msg.err != nil {
			m.errMsg = msg.err.Error()
		}
		return m, nil
	case actionResultMsg:
		if msg.err != nil {
			m.errMsg = msg.err.Error()
		} else {
			m.statusMsg = msg.message
		}
		return m, nil
	case tea.KeyMsg:
		if m.mode == tuiModeCommand {
			return m.handleCommandInput(msg)
		}
		return m.handleKey(msg)
	}

	return m, nil
}

func (m tuiModel) handleKey(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	switch msg.String() {
	case "q", "ctrl+c":
		m.quitting = true
		m.stopLogs()
		return m, tea.Quit
	case "up", "k":
		if len(m.servers) > 0 && m.selected > 0 {
			m.selected--
			return m, m.ensureLogStream()
		}
	case "down", "j":
		if len(m.servers) > 0 && m.selected < len(m.servers)-1 {
			m.selected++
			return m, m.ensureLogStream()
		}
	case "r":
		if m.client != nil {
			return m, loadServersCmd(m.client)
		}
	case "l":
		m.logsActive = !m.logsActive
		if m.logsActive {
			return m, m.ensureLogStream()
		}
		m.stopLogs()
		return m, nil
	case "s":
		return m, m.serverActionCmd("start")
	case "x":
		return m, m.serverActionCmd("stop")
	case "e":
		return m, m.serverActionCmd("restart")
	case "K":
		return m, m.serverActionCmd("kill")
	case "a":
		return m, m.stopAllCmd(300)
	case "c":
		m.mode = tuiModeCommand
		m.input.SetValue("")
		m.input.Focus()
		return m, textinput.Blink
	case "o":
		m.logSource = nextLogSource(m.logSource)
		if m.logsActive {
			return m, m.ensureLogStream()
		}
		return m, nil
	}

	return m, nil
}

func (m tuiModel) handleCommandInput(msg tea.KeyMsg) (tea.Model, tea.Cmd) {
	switch msg.String() {
	case "esc":
		m.mode = tuiModeNormal
		m.input.Blur()
		return m, nil
	case "enter":
		command := strings.TrimSpace(m.input.Value())
		m.mode = tuiModeNormal
		m.input.Blur()
		if command == "" {
			return m, nil
		}
		return m, m.consoleCommandCmd(command)
	}

	var cmd tea.Cmd
	m.input, cmd = m.input.Update(msg)
	return m, cmd
}

func (m tuiModel) View() string {
	if m.width == 0 || m.height == 0 {
		return "Loading..."
	}

	header := fmt.Sprintf("MineOS TUI  (source: %s  logs: %s)  [r] refresh  [l] logs  [o] source  [c] console  [q] quit",
		m.logSource,
		onOffLabel(m.logsActive),
	)

	leftWidth := m.width / 3
	if leftWidth < 20 {
		leftWidth = 20
	}
	if leftWidth > 40 {
		leftWidth = 40
	}

	rightWidth := m.width - leftWidth - 1
	if rightWidth < 20 {
		rightWidth = 20
	}

	contentHeight := m.height - 3
	if contentHeight < 5 {
		contentHeight = 5
	}

	leftLines := m.renderServers(leftWidth, contentHeight)
	rightLines := m.renderLogs(rightWidth, contentHeight)

	var b strings.Builder
	b.WriteString(trimToWidth(header, m.width))
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
		b.WriteString(padRight(left, leftWidth))
		b.WriteString(" ")
		b.WriteString(padRight(right, rightWidth))
		b.WriteString("\n")
	}

	b.WriteString(m.renderFooter())
	return b.String()
}

func (m tuiModel) renderServers(width, height int) []string {
	lines := make([]string, 0, height)
	lines = append(lines, trimToWidth("Servers", width))
	lines = append(lines, strings.Repeat("-", min(width, 7)))

	if m.errMsg != "" {
		lines = append(lines, trimToWidth("Error: "+m.errMsg, width))
	}

	if len(m.servers) == 0 {
		lines = append(lines, trimToWidth("No servers found.", width))
		return padLines(lines, height)
	}

	for i, server := range m.servers {
		prefix := " "
		if i == m.selected {
			prefix = ">"
		}
		line := fmt.Sprintf("%s %s [%s]", prefix, server.Name, server.Status)
		lines = append(lines, trimToWidth(line, width))
	}

	return padLines(lines, height)
}

func (m tuiModel) renderLogs(width, height int) []string {
	lines := make([]string, 0, height)
	title := "Logs"
	if m.selectedServer() != "" {
		title = fmt.Sprintf("Logs: %s", m.selectedServer())
	}
	lines = append(lines, trimToWidth(title, width))
	lines = append(lines, strings.Repeat("-", min(width, len(title))))

	if !m.logsActive {
		lines = append(lines, trimToWidth("Logs paused (press l to resume).", width))
		return padLines(lines, height)
	}

	if len(m.logs) == 0 {
		lines = append(lines, trimToWidth("Waiting for logs...", width))
		return padLines(lines, height)
	}

	start := 0
	if len(m.logs) > height-2 {
		start = len(m.logs) - (height - 2)
	}
	for _, line := range m.logs[start:] {
		lines = append(lines, trimToWidth(line, width))
	}

	return padLines(lines, height)
}

func (m tuiModel) renderFooter() string {
	var b strings.Builder
	if m.mode == tuiModeCommand {
		b.WriteString("Console> ")
		b.WriteString(m.input.View())
		b.WriteString("\n")
		return b.String()
	}

	if m.statusMsg != "" {
		b.WriteString(trimToWidth("Status: "+m.statusMsg, m.width))
		b.WriteString("\n")
		return b.String()
	}

	if m.errMsg != "" {
		b.WriteString(trimToWidth("Error: "+m.errMsg, m.width))
		b.WriteString("\n")
		return b.String()
	}

	b.WriteString(trimToWidth("Keys: up/down move  s start  x stop  e restart  K kill  a stop-all  c console  q quit", m.width))
	b.WriteString("\n")
	return b.String()
}

func (m tuiModel) ensureLogStream() tea.Cmd {
	if !m.logsActive || m.client == nil {
		return nil
	}
	server := m.selectedServer()
	if server == "" {
		return nil
	}
	m.stopLogs()
	ctx, cancel := context.WithCancel(context.Background())
	m.logCancel = cancel
	m.logsChan, m.logErrsChan = m.client.StreamConsoleLogs(ctx, server, m.logSource)
	return listenLogsCmd(m.logsChan, m.logErrsChan)
}

func (m *tuiModel) stopLogs() {
	if m.logCancel != nil {
		m.logCancel()
		m.logCancel = nil
	}
	m.logsChan = nil
	m.logErrsChan = nil
}

func (m tuiModel) selectedServer() string {
	if len(m.servers) == 0 || m.selected < 0 || m.selected >= len(m.servers) {
		return ""
	}
	return m.servers[m.selected].Name
}

func (m *tuiModel) appendLog(line string) {
	m.logs = append(m.logs, line)
	const maxLines = 500
	if len(m.logs) > maxLines {
		m.logs = m.logs[len(m.logs)-maxLines:]
	}
}

func (m tuiModel) serverActionCmd(action string) tea.Cmd {
	server := m.selectedServer()
	if server == "" || m.client == nil {
		return func() tea.Msg { return actionResultMsg{err: errors.New("select a server first")} }
	}
	return func() tea.Msg {
		uc := usecases.NewServerActionUseCase(m.client)
		err := uc.Execute(context.Background(), server, action)
		return actionResultMsg{message: fmt.Sprintf("%s: %s", action, server), err: err}
	}
}

func (m tuiModel) stopAllCmd(timeout int) tea.Cmd {
	if m.client == nil {
		return func() tea.Msg { return actionResultMsg{err: errors.New("API client not ready")} }
	}
	return func() tea.Msg {
		uc := usecases.NewStopAllServersUseCase(m.client)
		_, err := uc.Execute(context.Background(), timeout)
		return actionResultMsg{message: fmt.Sprintf("stop-all requested (timeout %ds)", timeout), err: err}
	}
}

func (m tuiModel) consoleCommandCmd(command string) tea.Cmd {
	server := m.selectedServer()
	if server == "" || m.client == nil {
		return func() tea.Msg { return actionResultMsg{err: errors.New("select a server first")} }
	}
	return func() tea.Msg {
		err := m.client.SendConsoleCommand(context.Background(), server, command)
		return actionResultMsg{message: fmt.Sprintf("sent to %s: %s", server, command), err: err}
	}
}

func loadConfigCmd(loadConfig *usecases.LoadConfigUseCase) tea.Cmd {
	return func() tea.Msg {
		cfg, err := loadConfig.Execute(context.Background())
		return configLoadedMsg{cfg: cfg, err: err}
	}
}

func loadServersCmd(client *api.Client) tea.Cmd {
	return func() tea.Msg {
		uc := usecases.NewListServersUseCase(client)
		servers, err := uc.Execute(context.Background())
		if err == nil {
			sort.Slice(servers, func(i, j int) bool { return servers[i].Name < servers[j].Name })
		}
		return serversLoadedMsg{servers: servers, err: err}
	}
}

func listenLogsCmd(logs <-chan api.LogEntry, errs <-chan error) tea.Cmd {
	if logs == nil && errs == nil {
		return nil
	}
	return func() tea.Msg {
		select {
		case entry, ok := <-logs:
			if !ok {
				return nil
			}
			return logEntryMsg{entry: entry}
		case err, ok := <-errs:
			if !ok {
				return nil
			}
			return logErrorMsg{err: err}
		}
	}
}

func formatLogEntry(entry api.LogEntry) string {
	if entry.Timestamp.IsZero() {
		return entry.Message
	}
	return fmt.Sprintf("[%s] %s", entry.Timestamp.Local().Format("15:04:05"), entry.Message)
}

func padRight(text string, width int) string {
	if width <= 0 {
		return ""
	}
	if len(text) >= width {
		return text[:width]
	}
	return text + strings.Repeat(" ", width-len(text))
}

func trimToWidth(text string, width int) string {
	if width <= 0 {
		return ""
	}
	if len(text) > width {
		return text[:width]
	}
	return text
}

func padLines(lines []string, height int) []string {
	for len(lines) < height {
		lines = append(lines, "")
	}
	return lines
}

func min(a, b int) int {
	if a < b {
		return a
	}
	return b
}

func onOffLabel(value bool) string {
	if value {
		return "on"
	}
	return "off"
}

func nextLogSource(current string) string {
	for i, source := range logSources {
		if source == current {
			return logSources[(i+1)%len(logSources)]
		}
	}
	return logSources[0]
}
