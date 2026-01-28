package tui

import (
	"context"
	"io"

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
)

// TuiMode represents the input mode of the TUI
type TuiMode int

const (
	ModeNormal TuiMode = iota
	ModeCommand
	ModeConfirm
	ModeInteractive // Running an interactive command inside the TUI
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

// TuiModel is the main model for the TUI application
type TuiModel struct {
	LoadConfig *usecases.LoadConfigUseCase
	Ctx        context.Context

	Width  int
	Height int

	Client *api.Client
	Cfg    config.Config

	Compose         *ComposeRunner
	ComposeReady    bool
	ConfigReady     bool
	ComposeError    string
	ComposeServices []string

	Servers       []ports.Server
	Selected      int  // Selected server in servers view
	ServerActions bool // Whether we're in server actions mode
	ActionIndex   int  // Selected action in server actions

	// Log state
	Logs            []string
	LogsActive      bool
	LogType         LogType
	LogSource       string
	MinecraftSource string // server name
	MinecraftType   string // combined|server|java|crash
	LogsChan        <-chan string
	LogErrsChan     <-chan error
	LogCancel       context.CancelFunc

	StatusMsg string
	ErrMsg    string

	// Navigation
	NavItems  []NavItem // Full navigation menu
	NavIndex  int       // Currently selected nav item
	NavScroll int       // Scroll offset for nav menu

	CurrentView  TuiView
	PreviousView TuiView

	// Command output display
	OutputLines []string
	OutputTitle string

	Mode     TuiMode
	Input    textinput.Model
	Quitting bool

	// Confirmation dialog state
	ConfirmAction  *MenuItem
	ConfirmMessage string

	// Interactive command state
	InteractiveStdin   io.WriteCloser
	InteractiveOutput  <-chan string
	InteractiveRunning bool

	// Streaming command state (output-only, no input)
	StreamingOutput  <-chan string
	StreamingRunning bool
	StreamingLabel   string

	// Retry state for error recovery
	RetryCount int

	// Container state tracking
	ContainersStopped bool // True when user intentionally stopped containers
}

// MenuItem represents an item in the command menu
type MenuItem struct {
	Label       string
	Args        []string
	Destructive bool // If true, requires confirmation
	Interactive bool // If true, requires user input (use tea.ExecProcess)
	Streaming   bool // If true, stream output in real-time (for long-running commands)
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

// LogLineMsg is sent for each log line received
type LogLineMsg struct {
	Line string
}

// LogErrorMsg is sent when a log streaming error occurs
type LogErrorMsg struct {
	Err error
}

// LogRetryMsg is sent to trigger log stream retry
type LogRetryMsg struct{}

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

// ConfirmActionMsg is sent when user confirms a destructive action
type ConfirmActionMsg struct {
	Confirmed bool
	Action    *MenuItem
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
}

// StreamingOutputMsg is sent for each line of streaming command output
type StreamingOutputMsg struct {
	Line string
}

// StreamingFinishedMsg is sent when a streaming command completes
type StreamingFinishedMsg struct {
	Label string
	Err   error
}
