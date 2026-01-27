package tui

import (
	"errors"
	"os"
	"os/exec"
	"sync"
)

// ComposeRunner wraps docker compose execution with thread safety
type ComposeRunner struct {
	Exe      string
	BaseArgs []string
	mu       sync.Mutex // Protects concurrent compose command execution
}

// DetectCompose detects the available docker compose command
func DetectCompose() (*ComposeRunner, error) {
	if _, err := exec.LookPath("docker"); err == nil {
		cmd := exec.Command("docker", "compose", "version")
		if err := cmd.Run(); err == nil {
			return &ComposeRunner{Exe: "docker", BaseArgs: []string{"compose"}}, nil
		}
	}

	if _, err := exec.LookPath("docker-compose"); err == nil {
		return &ComposeRunner{Exe: "docker-compose"}, nil
	}

	return nil, errors.New("docker compose is not available")
}

// Run executes a compose command with mutex protection
func (c *ComposeRunner) Run(args []string) error {
	c.mu.Lock()
	defer c.mu.Unlock()

	cmd := exec.Command(c.Exe, append(c.BaseArgs, args...)...)
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr
	cmd.Stdin = os.Stdin
	return cmd.Run()
}

// RunWithEnv executes a compose command with additional environment variables
func (c *ComposeRunner) RunWithEnv(args []string, env []string) error {
	c.mu.Lock()
	defer c.mu.Unlock()

	cmd := exec.Command(c.Exe, append(c.BaseArgs, args...)...)
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr
	cmd.Stdin = os.Stdin
	cmd.Env = append(os.Environ(), env...)
	return cmd.Run()
}

// RunSilent executes a compose command without outputting to stdout/stderr
func (c *ComposeRunner) RunSilent(args []string) ([]byte, error) {
	c.mu.Lock()
	defer c.mu.Unlock()

	cmd := exec.Command(c.Exe, append(c.BaseArgs, args...)...)
	return cmd.Output()
}
