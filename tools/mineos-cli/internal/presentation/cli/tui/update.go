package tui

import (
	"context"
	"io"
	"os/exec"
	"sort"
	"strings"
	"time"

	"github.com/charmbracelet/bubbles/textinput"
	tea "github.com/charmbracelet/bubbletea"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

// NewTuiModel creates a new TUI model with the given dependencies
func NewTuiModel(loadConfig *usecases.LoadConfigUseCase, ctx context.Context, version string) TuiModel {
	input := textinput.New()
	input.Placeholder = "console command"
	input.CharLimit = 2048
	input.Width = 50

	navItems := BuildNavItems()

	return TuiModel{
		LoadConfig:    loadConfig,
		Ctx:           ctx,
		Version:       version,
		LogsActive:    true,
		LogType:       LogTypeDocker,
		LogSource:     DefaultDockerLogSource,
		MinecraftType: "combined",
		Mode:          ModeNormal,
		CurrentView:   ViewDashboard,
		Input:         input,
		NavItems:      navItems,
		NavIndex:      FirstSelectableIndex(navItems),
	}
}

// RunTui runs the TUI application with context and I/O streams
func RunTui(ctx context.Context, loadConfig *usecases.LoadConfigUseCase, version string, in io.Reader, out io.Writer) error {
	model := NewTuiModel(loadConfig, ctx, version)

	opts := []tea.ProgramOption{tea.WithAltScreen()}
	if in != nil {
		opts = append(opts, tea.WithInput(in))
	}
	if out != nil {
		opts = append(opts, tea.WithOutput(out))
	}

	program := tea.NewProgram(model, opts...)

	// Handle context cancellation
	go func() {
		<-ctx.Done()
		program.Quit()
	}()

	_, err := program.Run()
	return err
}

// Init initializes the TUI model
func (m TuiModel) Init() tea.Cmd {
	return tea.Batch(m.LoadConfigCmd(), m.LoadComposeCmd())
}

// Update handles all incoming messages
func (m TuiModel) Update(msg tea.Msg) (tea.Model, tea.Cmd) {
	switch msg := msg.(type) {
	case tea.KeyMsg:
		if m.Mode == ModeCommand {
			return m.HandleCommandInput(msg)
		}
		if m.Mode == ModeConfirm {
			return m.HandleConfirmInput(msg)
		}
		if m.Mode == ModeInteractive {
			return m.HandleInteractiveInput(msg)
		}
		if m.Mode == ModeSearch {
			return m.HandleSearchInput(msg)
		}
		return m.HandleKey(msg)

	case tea.WindowSizeMsg:
		m.Width = msg.Width
		m.Height = msg.Height
		return m, nil

	case ConfigLoadedMsg:
		return m.handleConfigLoaded(msg)

	case ComposeLoadedMsg:
		return m.handleComposeLoaded(msg)

	case ComposeServicesMsg:
		return m.handleComposeServices(msg)

	case ServersLoadedMsg:
		return m.handleServersLoaded(msg)

	case LogStreamStartedMsg:
		return m.handleLogStreamStarted(msg)

	case LogLineMsg:
		m.AppendLog(msg.Line)
		// Clear log-related errors on successful log receipt
		if strings.Contains(m.ErrMsg, "log stream") || strings.Contains(m.ErrMsg, "stream") {
			m.ErrMsg = ""
		}
		return m, m.ListenLogsCmd()

	case LogErrorMsg:
		if msg.Err != nil {
			errStr := msg.Err.Error()
			// Log stream errors are transient - don't mark API as unhealthy
			// Ignore: context canceled, stream closed, signal killed, connection refused, etc.
			isTransientError := strings.Contains(errStr, "log stream") ||
				strings.Contains(errStr, "stream closed") ||
				strings.Contains(errStr, "context canceled") ||
				strings.Contains(errStr, "signal: killed") ||
				strings.Contains(errStr, "broken pipe") ||
				strings.Contains(errStr, "connection refused") ||
				strings.Contains(errStr, "connect: connection refused") ||
				strings.Contains(errStr, "no such host") ||
				strings.Contains(errStr, "i/o timeout")
			if !isTransientError {
				m.ErrMsg = errStr
			}

			// Don't retry if containers were intentionally stopped
			if m.ContainersStopped {
				return m, nil
			}

			// Retry streaming if active and not quitting (with longer delay for connection errors)
			if m.LogsActive && !m.Quitting && !strings.Contains(errStr, "context canceled") {
				// Use longer delay for connection errors to reduce flickering
				delay := LogRetryDelay
				if strings.Contains(errStr, "connection refused") || strings.Contains(errStr, "no such host") {
					delay = LogRetryDelay * 3 // 6 seconds for connection errors
				}
				return m, tea.Tick(delay, func(time.Time) tea.Msg {
					return LogRetryMsg{}
				})
			}
		}
		return m, nil

	case LogRetryMsg:
		return m, m.StartLogStreamCmd()

	case ActionResultMsg:
		return m.handleActionResult(msg)

	case ExecFinishedMsg:
		return m.handleExecFinished(msg)

	case ConfirmActionMsg:
		return m.handleConfirmAction(msg)

	case InteractiveStartedMsg:
		return m.handleInteractiveStarted(msg)

	case InteractiveOutputMsg:
		return m.handleInteractiveOutput(msg)

	case InteractiveFinishedMsg:
		return m.handleInteractiveFinished(msg)

	case StreamingStartedMsg:
		return m.handleStreamingStarted(msg)

	case StreamingOutputMsg:
		return m.handleStreamingOutput(msg)

	case StreamingFinishedMsg:
		return m.handleStreamingFinished(msg)

	case HealthTickMsg:
		// Don't poll if containers are intentionally stopped or already healthy
		if m.ContainersStopped {
			return m, nil
		}
		if m.ConfigReady && m.ErrMsg == "" {
			return m, nil
		}
		// Re-attempt config and server load
		m.StatusMsg = "Reconnecting to API..."
		return m, tea.Batch(m.LoadConfigCmd(), m.LoadServersCmd())

	case SettingsToggledMsg:
		if msg.Err != nil {
			m.ErrMsg = "Failed to update setting: " + msg.Err.Error()
			return m, nil
		}
		if msg.Key == "MINEOS_CLI_PRERELEASE_UPDATES" {
			m.Cfg.PreReleaseUpdates = msg.Val
			if msg.Val == "true" {
				m.StatusMsg = "Update channel: Preview — builds may be unstable"
			} else {
				m.StatusMsg = "Update channel: Stable"
			}
		}
		m.ErrMsg = ""
		return m, nil
	}

	return m, nil
}

// scheduleHealthPoll returns a tea.Cmd that sends a HealthTickMsg after HealthPollInterval
func scheduleHealthPoll() tea.Cmd {
	return tea.Tick(HealthPollInterval, func(time.Time) tea.Msg {
		return HealthTickMsg{}
	})
}

func (m TuiModel) handleConfigLoaded(msg ConfigLoadedMsg) (tea.Model, tea.Cmd) {
	if msg.Err != nil {
		m.ErrMsg = msg.Err.Error()
		// Retry logic with backoff
		if msg.RetryCount < MaxLogRetries {
			m.StatusMsg = "Config load failed, retrying..."
			return m, tea.Tick(LogRetryDelay*time.Duration(msg.RetryCount+1), func(time.Time) tea.Msg {
				return m.LoadConfigCmdWithRetry(msg.RetryCount + 1)()
			})
		}
		// All retries exhausted — schedule periodic health poll to recover later
		m.StatusMsg = "API unavailable, will retry periodically..."
		return m, scheduleHealthPoll()
	}
	m.Cfg = msg.Cfg
	m.ConfigReady = true
	m.ErrMsg = ""    // Clear previous errors
	m.StatusMsg = "" // Clear reconnecting status
	m.RetryCount = 0
	m.Client = api.NewClientFromConfig(msg.Cfg)
	return m, m.LoadServersCmd()
}

func (m TuiModel) handleComposeLoaded(msg ComposeLoadedMsg) (tea.Model, tea.Cmd) {
	if msg.Err != nil {
		m.ComposeError = msg.Err.Error()
		return m, nil
	}
	m.Compose = msg.Compose
	m.ComposeReady = true
	return m, tea.Batch(m.LoadComposeServicesCmd(), m.StartLogStreamCmd())
}

func (m TuiModel) handleComposeServices(msg ComposeServicesMsg) (tea.Model, tea.Cmd) {
	if msg.Err != nil {
		m.ComposeError = msg.Err.Error()
		return m, m.StartLogStreamCmd()
	}
	m.ComposeServices = NormalizeComposeServices(msg.Services)
	if m.LogSource == "" {
		m.LogSource = DefaultDockerLogSource
	}
	return m, m.StartLogStreamCmd()
}

func (m TuiModel) handleServersLoaded(msg ServersLoadedMsg) (tea.Model, tea.Cmd) {
	if msg.Err != nil {
		errStr := msg.Err.Error()
		// Don't show transient connection errors in status
		isTransient := strings.Contains(errStr, "connection refused") ||
			strings.Contains(errStr, "no such host") ||
			strings.Contains(errStr, "i/o timeout")
		if !isTransient {
			m.ErrMsg = errStr
		}
		// Schedule health poll to retry when API comes back
		if !m.ContainersStopped {
			return m, scheduleHealthPoll()
		}
		return m, nil
	}
	if msg.Cfg.EnvPath != "" {
		m.Cfg = msg.Cfg
		m.Client = api.NewClientFromConfig(msg.Cfg)
		m.ConfigReady = true
	}
	m.Servers = msg.Servers
	if len(m.Servers) == 0 {
		m.Selected = 0
		return m, nil
	}
	if m.Selected >= len(m.Servers) {
		m.Selected = len(m.Servers) - 1
	}
	if m.MinecraftSource == "" && len(m.Servers) > 0 {
		m.MinecraftSource = m.SelectedServer()
	}
	return m, nil
}

func (m TuiModel) handleLogStreamStarted(msg LogStreamStartedMsg) (tea.Model, tea.Cmd) {
	// Store the new stream state
	m.LogsChan = msg.LogsChan
	m.LogErrsChan = msg.ErrsChan
	m.LogCancel = msg.Cancel

	// Clear logs when starting a new stream to prevent duplicates on reconnect
	// The API sends all logs from the beginning, so we must clear to avoid accumulation
	m.Logs = nil

	return m, m.ListenLogsCmd()
}

func (m TuiModel) handleActionResult(msg ActionResultMsg) (tea.Model, tea.Cmd) {
	if msg.Cfg != nil {
		m.Cfg = *msg.Cfg
		m.Client = api.NewClientFromConfig(*msg.Cfg)
		m.ConfigReady = true
	}
	if msg.Err != nil {
		m.ErrMsg = msg.Err.Error()
	} else {
		m.StatusMsg = msg.Message
		m.ErrMsg = "" // Clear error on success
	}
	return m, nil
}

func (m TuiModel) handleExecFinished(msg ExecFinishedMsg) (tea.Model, tea.Cmd) {
	// Update output display
	if len(msg.Output) > 0 {
		m.OutputLines = append(m.OutputLines, msg.Output...)
	}

	if msg.Err != nil {
		m.OutputLines = append(m.OutputLines, "", "Error: "+msg.Err.Error())
		m.ErrMsg = msg.Err.Error()
	} else if msg.Action != "" {
		m.OutputLines = append(m.OutputLines, "", "✓ "+msg.Action+" complete")
		m.StatusMsg = msg.Action + " complete"
		m.ErrMsg = "" // Clear error on success
	}

	// Add hint to go back
	m.OutputLines = append(m.OutputLines, "", "Press Esc to go back.")

	return m, tea.Batch(m.LoadConfigCmd(), m.LoadComposeCmd(), m.LoadServersCmd())
}

func (m TuiModel) handleConfirmAction(msg ConfirmActionMsg) (tea.Model, tea.Cmd) {
	m.Mode = ModeNormal
	m.ConfirmAction = nil
	m.ConfirmMessage = ""

	if !msg.Confirmed || msg.Action == nil {
		m.StatusMsg = "Action cancelled"
		return m, nil
	}

	return m, m.ExecMenuItem(*msg.Action)
}

func (m TuiModel) handleInteractiveStarted(msg InteractiveStartedMsg) (tea.Model, tea.Cmd) {
	m.Mode = ModeInteractive
	m.InteractiveStdin = msg.Stdin
	m.InteractiveOutput = msg.Output
	m.InteractiveRunning = true
	m.Input.SetValue("")
	m.Input.Focus()
	return m, tea.Batch(m.ListenInteractiveCmd(), textinput.Blink)
}

func (m TuiModel) handleInteractiveOutput(msg InteractiveOutputMsg) (tea.Model, tea.Cmd) {
	m.OutputLines = append(m.OutputLines, msg.Line)
	// Limit output buffer
	if len(m.OutputLines) > MaxLogLines {
		m.OutputLines = m.OutputLines[len(m.OutputLines)-MaxLogLines:]
	}
	return m, m.ListenInteractiveCmd()
}

func (m TuiModel) handleInteractiveFinished(msg InteractiveFinishedMsg) (tea.Model, tea.Cmd) {
	m.Mode = ModeNormal
	m.InteractiveRunning = false
	if m.InteractiveStdin != nil {
		m.InteractiveStdin.Close()
		m.InteractiveStdin = nil
	}
	m.InteractiveOutput = nil
	m.Input.Blur()

	if msg.Err != nil {
		m.OutputLines = append(m.OutputLines, "", "Error: "+msg.Err.Error())
		m.ErrMsg = msg.Err.Error()
	} else {
		m.OutputLines = append(m.OutputLines, "", "✓ Command complete")
		m.ErrMsg = ""
	}
	m.OutputLines = append(m.OutputLines, "", "Press Esc to go back.")

	return m, tea.Batch(m.LoadConfigCmd(), m.LoadComposeCmd(), m.LoadServersCmd())
}

// AppendLog adds a line to the log buffer with size limiting
func (m *TuiModel) AppendLog(line string) {
	m.Logs = append(m.Logs, line)
	if len(m.Logs) > MaxLogLines {
		m.Logs = m.Logs[len(m.Logs)-MaxLogLines:]
	}
}

// LoadConfigCmd creates a command to load configuration
func (m TuiModel) LoadConfigCmd() tea.Cmd {
	return m.LoadConfigCmdWithRetry(0)
}

// LoadConfigCmdWithRetry creates a command to load configuration with retry count
func (m TuiModel) LoadConfigCmdWithRetry(retryCount int) tea.Cmd {
	return func() tea.Msg {
		ctx := m.Ctx
		if ctx == nil {
			ctx = context.Background()
		}
		cfg, err := m.LoadConfig.Execute(ctx)
		return ConfigLoadedMsg{Cfg: cfg, Err: err, RetryCount: retryCount}
	}
}

// LoadComposeCmd creates a command to detect docker compose
func (m TuiModel) LoadComposeCmd() tea.Cmd {
	return func() tea.Msg {
		compose, err := DetectCompose()
		return ComposeLoadedMsg{Compose: compose, Err: err}
	}
}

// LoadComposeServicesCmd creates a command to list compose services
func (m TuiModel) LoadComposeServicesCmd() tea.Cmd {
	return func() tea.Msg {
		services, err := m.ListComposeServices()
		return ComposeServicesMsg{Services: services, Err: err}
	}
}

// ListComposeServices lists all services defined in docker-compose
func (m TuiModel) ListComposeServices() ([]string, error) {
	args := append([]string{}, m.Compose.BaseArgs...)
	args = append(args, "config", "--services")
	cmd := exec.Command(m.Compose.Exe, args...)
	output, err := cmd.Output()
	if err != nil {
		return nil, err
	}

	lines := strings.Split(string(output), "\n")
	services := make([]string, 0, len(lines))
	for _, line := range lines {
		line = strings.TrimSpace(line)
		if line == "" {
			continue
		}
		services = append(services, line)
	}

	return services, nil
}

// LoadServersCmd creates a command to load the server list
func (m TuiModel) LoadServersCmd() tea.Cmd {
	return func() tea.Msg {
		ctx := m.Ctx
		if ctx == nil {
			ctx = context.Background()
		}
		uc := usecases.NewListServersUseCase(m.Client)
		servers, err := uc.Execute(ctx)
		if err != nil {
			return ServersLoadedMsg{Err: err}
		}
		sort.Slice(servers, func(i, j int) bool { return servers[i].Name < servers[j].Name })
		return ServersLoadedMsg{Servers: servers, Cfg: m.Cfg}
	}
}

// StartLogStreamCmd creates a command to start log streaming
// This uses message-based state update to avoid the value receiver issue
func (m TuiModel) StartLogStreamCmd() tea.Cmd {
	return func() tea.Msg {
		// Stop any existing stream first
		if m.LogCancel != nil {
			m.LogCancel()
		}

		if !m.LogsActive {
			return nil
		}

		ctx, cancel := context.WithCancel(context.Background())

		var logsChan <-chan string
		var errsChan <-chan error
		logSource := m.LogSource

		if m.LogType == LogTypeDocker {
			if !m.ComposeReady {
				cancel()
				return nil
			}
			logsChan, errsChan = StreamDockerLogs(ctx, m.Compose, m.LogSource)
		} else {
			if !m.ConfigReady || m.MinecraftSource == "" {
				cancel()
				return nil
			}
			logsChan, errsChan = StreamMinecraftLogs(ctx, m.Client, m.MinecraftSource, m.MinecraftType)
			logSource = m.MinecraftSource
		}

		return LogStreamStartedMsg{
			LogsChan:  logsChan,
			ErrsChan:  errsChan,
			Cancel:    cancel,
			LogType:   m.LogType,
			LogSource: logSource,
		}
	}
}

// EnsureLogStreamCmd is deprecated - use StartLogStreamCmd instead
// Kept for backward compatibility
func (m TuiModel) EnsureLogStreamCmd() tea.Cmd {
	return m.StartLogStreamCmd()
}

// ListenLogsCmd creates a command to listen for log messages
func (m TuiModel) ListenLogsCmd() tea.Cmd {
	logsChan := m.LogsChan
	errsChan := m.LogErrsChan

	if logsChan == nil && errsChan == nil {
		return nil
	}

	return func() tea.Msg {
		select {
		case line, ok := <-logsChan:
			if !ok {
				// Channel closed cleanly - trigger silent retry
				return LogRetryMsg{}
			}
			return LogLineMsg{Line: line}
		case err, ok := <-errsChan:
			if !ok {
				// Channel closed cleanly - trigger silent retry
				return LogRetryMsg{}
			}
			return LogErrorMsg{Err: err}
		}
	}
}

// NormalizeComposeServices normalizes the list of compose services
func NormalizeComposeServices(services []string) []string {
	if len(services) == 0 {
		return []string{DefaultDockerLogSource}
	}
	unique := map[string]struct{}{}
	for _, service := range services {
		service = strings.TrimSpace(service)
		if service == "" {
			continue
		}
		unique[service] = struct{}{}
	}
	list := make([]string, 0, len(unique))
	for service := range unique {
		list = append(list, service)
	}
	sort.Strings(list)
	return append([]string{DefaultDockerLogSource}, list...)
}

// RequestConfirmation sets up a confirmation dialog for a destructive action
func (m *TuiModel) RequestConfirmation(action *MenuItem, message string) {
	m.Mode = ModeConfirm
	m.ConfirmAction = action
	m.ConfirmMessage = message
}

// handleStreamingStarted handles the start of a streaming command
func (m TuiModel) handleStreamingStarted(msg StreamingStartedMsg) (tea.Model, tea.Cmd) {
	m.StreamingOutput = msg.Output
	m.StreamingRunning = true
	m.StreamingLabel = msg.Label

	// Switch to output view
	m.PreviousView = m.CurrentView
	m.CurrentView = ViewOutput
	m.OutputTitle = msg.Label
	m.OutputLines = nil // Clear and let streaming populate

	return m, m.ListenStreamingCmd()
}

// handleStreamingOutput handles a line of streaming output
func (m TuiModel) handleStreamingOutput(msg StreamingOutputMsg) (tea.Model, tea.Cmd) {
	m.OutputLines = append(m.OutputLines, msg.Line)
	// Limit output buffer
	if len(m.OutputLines) > MaxLogLines {
		m.OutputLines = m.OutputLines[len(m.OutputLines)-MaxLogLines:]
	}
	return m, m.ListenStreamingCmd()
}

// handleStreamingFinished handles the completion of a streaming command
func (m TuiModel) handleStreamingFinished(msg StreamingFinishedMsg) (tea.Model, tea.Cmd) {
	m.StreamingRunning = false
	m.StreamingOutput = nil

	// Detect if containers were intentionally stopped
	isStopAction := strings.Contains(msg.Label, "Stop") || strings.Contains(msg.Label, "Remove")
	isStartAction := strings.Contains(msg.Label, "Start") || strings.Contains(msg.Label, "Restart")

	if msg.Err != nil {
		m.OutputLines = append(m.OutputLines, "", "Error: "+msg.Err.Error())
		m.ErrMsg = msg.Err.Error()
	} else {
		m.OutputLines = append(m.OutputLines, "", "✓ "+msg.Label+" complete")
		m.StatusMsg = msg.Label + " complete"
		m.ErrMsg = ""

		// Track container state
		if isStopAction {
			m.ContainersStopped = true
			m.ConfigReady = false // API is no longer available
			m.Servers = nil
		} else if isStartAction {
			m.ContainersStopped = false
		}
	}
	m.OutputLines = append(m.OutputLines, "", "Press Esc to go back.")

	// Don't try to load servers if we just stopped containers
	if m.ContainersStopped {
		return m, m.LoadComposeCmd() // Only reload compose status
	}

	return m, tea.Batch(m.LoadConfigCmd(), m.LoadComposeCmd(), m.LoadServersCmd())
}

// ListenStreamingCmd creates a command to listen for streaming output
func (m TuiModel) ListenStreamingCmd() tea.Cmd {
	outputChan := m.StreamingOutput
	label := m.StreamingLabel

	if outputChan == nil {
		return nil
	}

	return func() tea.Msg {
		line, ok := <-outputChan
		if !ok {
			// Channel closed - command finished
			return StreamingFinishedMsg{Label: label}
		}
		return StreamingOutputMsg{Line: line}
	}
}
