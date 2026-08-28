package tui

import (
	"context"
	"io"

	"github.com/charmbracelet/bubbles/spinner"
	"github.com/charmbracelet/bubbles/textinput"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/ports"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

// TuiView represents the different views in the TUI
type TuiView int

const (
	ViewDashboard TuiView = iota
	ViewServers
	ViewServiceLogs // Docker container logs
	ViewSettings
	ViewOutput // Shows command output
	ViewHealth // Watchdog + alert roll-up
)

// TuiMode represents the input mode of the TUI
type TuiMode int

const (
	ModeNormal TuiMode = iota
	ModeCommand
	ModeConfirm
	ModeInteractive // Running an interactive command inside the TUI
	ModeSearch      // Searching logs
)

// LogType represents the type of log being viewed
type LogType int

const (
	LogTypeDocker LogType = iota
	LogTypeMinecraft
)

// NavItemType represents the type of navigation item
type NavItemType int

const (
	NavView NavItemType = iota
	NavAction
	NavHeader
	NavSeparator
)

// NavItem represents an item in the unified navigation menu
type NavItem struct {
	Label       string
	ItemType    NavItemType
	View        TuiView   // For NavView items
	Action      *MenuItem // For NavAction items
	Destructive bool
}

// ConnectionState holds API/compose connectivity and configuration.
type ConnectionState struct {
	Client *api.Client
	Cfg    config.Config

	Compose         *ComposeRunner
	ComposeReady    bool
	ConfigReady     bool
	Healthy         bool // Last real /health result (drives the header badge)
	ComposeError    string
	ComposeServices []string

	ContainersStopped bool // True when user intentionally stopped containers
	RetryCount        int  // Config-load retry state
}

// ServerListState holds the servers table and its selection.
type ServerListState struct {
	Servers           []ports.Server
	Selected          int  // Selected server in servers view
	ServerActions     bool // Whether we're in server actions mode
	ActionIndex       int  // Selected action in server actions
	ServersLoadedOnce bool // Distinguishes "still loading" from "genuinely empty"
}

// LogState holds both log subsystems (docker + minecraft) and log-view UI state.
type LogState struct {
	Logs            []string
	LogsActive      bool
	LogType         LogType
	LogSource       string
	MinecraftSource string // server name
	MinecraftType   string // combined|server|java|crash
	LogsChan        <-chan string
	LogErrsChan     <-chan error
	LogCancel       context.CancelFunc

	LogScroll      int    // Scroll offset for logs view
	LogSearchQuery string // Search query for logs
	LogSearchMode  bool   // Whether in search mode
	LogRetries     int    // Consecutive clean-close reconnects without data (resets on receipt)
}

// PerfState holds the live per-server performance stream and its history buffer.
type PerfState struct {
	PerfSample *api.PerfSample
	PerfChan   <-chan api.PerfSample
	PerfErrs   <-chan error
	PerfCancel context.CancelFunc
	PerfServer string

	// Sample history backing the sparkline: history-endpoint backfill plus
	// live samples appended as they stream in. Cleared with the stream.
	PerfHistory []api.PerfSample
}

// HealthState holds the watchdog/crash/alert roll-up for the health view.
type HealthState struct {
	Watchdog         map[string]api.WatchdogServerStatus
	Crashes          []api.CrashEvent
	Alerts           []api.Notification
	HealthDataLoaded bool   // First fetch completed (distinguishes loading from empty)
	HealthDataErr    string // Last fetch error, if any
}

// OutputState holds the output view plus streaming/interactive subprocess state.
type OutputState struct {
	OutputLines []string
	OutputTitle string

	StreamingOutput  <-chan string
	StreamingRunning bool
	StreamingLabel   string
	StreamingEffect  ContainerEffect

	InteractiveStdin   io.WriteCloser
	InteractiveOutput  <-chan string
	InteractiveRunning bool
}

// NavState holds the sidebar menu and current/previous view.
type NavState struct {
	NavItems  []NavItem // Full navigation menu
	NavIndex  int       // Currently selected nav item
	NavScroll int       // Scroll offset for nav menu

	CurrentView  TuiView
	PreviousView TuiView
}

// DialogState holds modal state: input mode, text input, confirmations, help.
type DialogState struct {
	Mode  TuiMode
	Input textinput.Model

	// ConfirmAction is a pending subprocess command (stack ops);
	// ConfirmServerName/Action is a pending in-process server action (kill) —
	// exactly one is set while confirming.
	ConfirmAction       *MenuItem
	ConfirmMessage      string
	ConfirmServerName   string
	ConfirmServerAction string

	ShowHelp bool // Help overlay visibility (toggled with '?')
}

// StatusState holds the transient status/error lines and their TTL bookkeeping.
type StatusState struct {
	StatusMsg string
	ErrMsg    string

	// Last status/error seen by the TTL sweep: a message that survives one full
	// poll interval unchanged is cleared (persistent conditions re-set theirs).
	StatusSeenAtTick string
	ErrSeenAtTick    string
}

// TuiModel is the main model for the TUI application. State is grouped into
// embedded sub-models per domain; Go field promotion keeps accessors flat
// (m.Servers, m.CurrentView, ...), while each group can be reasoned about —
// and reset — as a unit.
type TuiModel struct {
	LoadConfig *usecases.LoadConfigUseCase
	Ctx        context.Context

	// Version is the mineos-cli version (usually from the git tag at build time).
	Version string

	Width  int
	Height int

	ConnectionState
	ServerListState
	LogState
	PerfState
	HealthState
	OutputState
	NavState
	DialogState
	StatusState

	// Spinner shown while work is in flight (connecting, streaming, interactive)
	Spinner spinner.Model

	Quitting bool
}

// ContainerEffect declares how a finished command changed container state,
// replacing label-substring inference.
type ContainerEffect int

const (
	EffectNone ContainerEffect = iota
	EffectStartsContainers
	EffectStopsContainers
)

// MenuItem represents an item in the command menu
type MenuItem struct {
	Label       string
	Args        []string
	Destructive bool            // If true, requires confirmation
	Interactive bool            // If true, requires user input (use tea.ExecProcess)
	Streaming   bool            // If true, stream output in real-time (for long-running commands)
	Console     bool            // If true, opens the console-command prompt instead of executing
	Effect      ContainerEffect // Container-state change applied when the command succeeds
}

// Message types for Bubble Tea event handling

// ConfigLoadedMsg is sent when config loading completes
type ConfigLoadedMsg struct {
	Cfg        config.Config
	Err        error
	RetryCount int
}

// ComposeLoadedMsg is sent when compose detection completes
type ComposeLoadedMsg struct {
	Compose *ComposeRunner
	Err     error
}

// ComposeServicesMsg is sent when compose services are loaded
type ComposeServicesMsg struct {
	Services []string
	Err      error
}

// ServersLoadedMsg is sent when server list is loaded
type ServersLoadedMsg struct {
	Servers []ports.Server
	Cfg     config.Config
	Err     error
}

// LogStreamStartedMsg is sent when a new log stream is started
type LogStreamStartedMsg struct {
	LogsChan  <-chan string
	ErrsChan  <-chan error
	Cancel    context.CancelFunc
	LogType   LogType
	LogSource string
}

// LogLinesMsg carries a batch of log lines — all lines available on the
// channel are drained into one message so a startup burst costs one render.
type LogLinesMsg struct {
	Lines []string
}

// LogStreamClosedMsg signals the log channel closed cleanly (EOF, not error)
type LogStreamClosedMsg struct{}

// LogErrorMsg is sent when a log streaming error occurs
type LogErrorMsg struct {
	Err error
}

// LogRetryMsg is sent to trigger log stream retry
type LogRetryMsg struct{}

// ServerActionDoneMsg reports an in-process server action (start/stop/restart/kill)
type ServerActionDoneMsg struct {
	Server string
	Action string
	Err    error
}

// ActionResultMsg is sent when an action completes
type ActionResultMsg struct {
	Message string
	Err     error
	Cfg     *config.Config
}

// ExecFinishedMsg is sent when an external command finishes
type ExecFinishedMsg struct {
	Action string
	Output []string
	Err    error
}

// InteractiveStartedMsg is sent when an interactive command starts
type InteractiveStartedMsg struct {
	Stdin  io.WriteCloser
	Output <-chan string
}

// InteractiveOutputMsg is sent for each line of interactive command output
type InteractiveOutputMsg struct {
	Line string
}

// InteractiveFinishedMsg is sent when an interactive command completes
type InteractiveFinishedMsg struct {
	Err error
}

// StreamingStartedMsg is sent when a streaming (output-only) command starts
type StreamingStartedMsg struct {
	Output <-chan string
	Label  string
	Effect ContainerEffect
}

// StreamingOutputMsg is sent for each line of streaming command output
type StreamingOutputMsg struct {
	Line string
}

// StreamingFinishedMsg is sent when a streaming command completes
type StreamingFinishedMsg struct {
	Label  string
	Effect ContainerEffect
	Err    error
}

// SettingsToggledMsg is sent when a setting is toggled in the TUI
type SettingsToggledMsg struct {
	Key string
	Val string
	Err error
}

// HealthTickMsg is sent periodically to drive the live refresh loop
type HealthTickMsg struct{}

// HealthCheckedMsg carries the result of a real /health probe
type HealthCheckedMsg struct {
	Healthy bool
	Err     error
}

// HealthDataMsg carries the watchdog/crash/alert roll-up for the health view
type HealthDataMsg struct {
	Watchdog map[string]api.WatchdogServerStatus
	Crashes  []api.CrashEvent
	Alerts   []api.Notification
	Err      error
}

// PerfStreamStartedMsg carries the channels for a freshly opened perf stream
type PerfStreamStartedMsg struct {
	Server  string
	Samples <-chan api.PerfSample
	Errs    <-chan error
	Cancel  context.CancelFunc
}

// PerfSampleMsg carries one live performance sample
type PerfSampleMsg struct{ Sample api.PerfSample }

// PerfErrorMsg signals the perf stream errored or ended
type PerfErrorMsg struct{ Err error }

// PerfHistoryMsg carries the history backfill for the metrics sparkline
type PerfHistoryMsg struct {
	Server  string
	Samples []api.PerfSample
	Err     error
}
