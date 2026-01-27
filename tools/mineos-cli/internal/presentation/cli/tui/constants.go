package tui

import "time"

// Log buffer and streaming constants
const (
	MaxLogLines          = 700
	DefaultDockerLogTail = 200
	LogRetryDelay        = 2 * time.Second
	MaxLogRetries        = 3
)

// Timeout constants
const (
	DefaultStopAllTimeout    = 300
	DefaultShutdownTimeout   = 300
	DefaultAPIHealthTimeout  = 60
)

// UI layout constants
const (
	SidebarWidth    = 20
	MinContentHeight = 5
)

// Default source for docker logs
const DefaultDockerLogSource = "all"

// Minecraft log types
var MinecraftLogTypes = []string{"combined", "server", "java", "crash"}
