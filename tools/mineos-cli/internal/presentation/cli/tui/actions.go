package tui

import (
	"errors"
	"fmt"
	"os"
	"os/exec"
	"strings"

	tea "github.com/charmbracelet/bubbletea"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
)

func (m TuiModel) ServerActionCmd(action string) tea.Cmd {
	server := m.SelectedServer()
	if server == "" || !m.ConfigReady {
		return func() tea.Msg { return ActionResultMsg{Err: errors.New("select a server first")} }
	}
	ctx := m.Ctx
	client := m.Client
	return func() tea.Msg {
		uc := usecases.NewServerActionUseCase(client)
		err := uc.Execute(ctx, server, action)
		return ActionResultMsg{
			Message: fmt.Sprintf("%s: %s", action, server),
			Err:     err,
		}
	}
}

func (m TuiModel) StopAllCmd(timeout int) tea.Cmd {
	if !m.ConfigReady {
		return func() tea.Msg { return ActionResultMsg{Err: errors.New("API client not ready")} }
	}
	ctx := m.Ctx
	client := m.Client
	return func() tea.Msg {
		uc := usecases.NewStopAllServersUseCase(client)
		_, err := uc.Execute(ctx, timeout)
		return ActionResultMsg{
			Message: fmt.Sprintf("stop-all requested (timeout %ds)", timeout),
			Err:     err,
		}
	}
}

func (m TuiModel) ConsoleCommandCmd(command string) tea.Cmd {
	server := m.SelectedServer()
	if server == "" || !m.ConfigReady {
		return func() tea.Msg { return ActionResultMsg{Err: errors.New("select a server first")} }
	}
	ctx := m.Ctx
	client := m.Client
	return func() tea.Msg {
		err := client.SendConsoleCommand(ctx, server, command)
		return ActionResultMsg{
			Message: fmt.Sprintf("sent to %s: %s", server, command),
			Err:     err,
		}
	}
}

func (m TuiModel) ComposeActionCmd(args ...string) tea.Cmd {
	if !m.ComposeReady {
		return func() tea.Msg { return ActionResultMsg{Err: errors.New("docker compose not available")} }
	}
	return func() tea.Msg {
		err := m.Compose.Run(args)
		action := strings.Join(args, " ")
		return ActionResultMsg{Message: "compose " + action + " requested", Err: err}
	}
}

func (m TuiModel) ExecMenuItem(item MenuItem) tea.Cmd {
	exe, err := os.Executable()
	if err != nil {
		return func() tea.Msg { return ExecFinishedMsg{Action: item.Label, Err: err} }
	}
	args := append([]string{}, item.Args...)
	if envPath := strings.TrimSpace(m.Cfg.EnvPath); envPath != "" && envPath != ".env" {
		args = append([]string{"--env", envPath}, args...)
	}

	// Interactive commands use tea.ExecProcess to suspend TUI and give full terminal control
	if item.Interactive {
		cmd := exec.Command(exe, args...)
		label := item.Label
		return tea.ExecProcess(cmd, func(err error) tea.Msg {
			return ExecFinishedMsg{Action: label, Err: err}
		})
	}

	// Streaming commands show output in real-time (for long-running docker operations)
	if item.Streaming {
		return m.StartStreamingCmd(exe, args, item.Label)
	}

	// Non-interactive commands capture output for display in TUI
	return func() tea.Msg {
		cmd := exec.Command(exe, args...)
		output, err := cmd.CombinedOutput()

		// Parse output into lines
		outputStr := strings.TrimSpace(string(output))
		var lines []string
		if outputStr != "" {
			lines = strings.Split(outputStr, "\n")
		}

		return ExecFinishedMsg{Action: item.Label, Output: lines, Err: err}
	}
}

// StartStreamingCmd starts a command that streams output without requiring stdin
func (m TuiModel) StartStreamingCmd(exe string, args []string, label string) tea.Cmd {
	return func() tea.Msg {
		cmd := exec.Command(exe, args...)

		stdout, err := cmd.StdoutPipe()
		if err != nil {
			return ExecFinishedMsg{Action: label, Err: err}
		}

		stderr, err := cmd.StderrPipe()
		if err != nil {
			stdout.Close()
			return ExecFinishedMsg{Action: label, Err: err}
		}

		if err := cmd.Start(); err != nil {
			stdout.Close()
			stderr.Close()
			return ExecFinishedMsg{Action: label, Err: err}
		}

		// Create output channel and start readers
		outputChan := make(chan string, 100)

		// Read stdout
		go func() {
			buf := make([]byte, 1024)
			for {
				n, err := stdout.Read(buf)
				if n > 0 {
					lines := strings.Split(string(buf[:n]), "\n")
					for _, line := range lines {
						if line != "" {
							outputChan <- line
						}
					}
				}
				if err != nil {
					break
				}
			}
		}()

		// Read stderr
		go func() {
			buf := make([]byte, 1024)
			for {
				n, err := stderr.Read(buf)
				if n > 0 {
					lines := strings.Split(string(buf[:n]), "\n")
					for _, line := range lines {
						if line != "" {
							outputChan <- line
						}
					}
				}
				if err != nil {
					break
				}
			}
		}()

		// Wait for command to finish in background
		go func() {
			cmd.Wait()
			close(outputChan)
		}()

		return StreamingStartedMsg{
			Output: outputChan,
			Label:  label,
		}
	}
}

// StartInteractiveCmd starts an interactive command with piped I/O
func (m TuiModel) StartInteractiveCmd(exe string, args []string, label string) tea.Cmd {
	return func() tea.Msg {
		cmd := exec.Command(exe, args...)

		stdin, err := cmd.StdinPipe()
		if err != nil {
			return ExecFinishedMsg{Action: label, Err: err}
		}

		stdout, err := cmd.StdoutPipe()
		if err != nil {
			stdin.Close()
			return ExecFinishedMsg{Action: label, Err: err}
		}

		stderr, err := cmd.StderrPipe()
		if err != nil {
			stdin.Close()
			stdout.Close()
			return ExecFinishedMsg{Action: label, Err: err}
		}

		if err := cmd.Start(); err != nil {
			stdin.Close()
			stdout.Close()
			stderr.Close()
			return ExecFinishedMsg{Action: label, Err: err}
		}

		// Create output channel and start readers
		outputChan := make(chan string, 100)

		// Read stdout
		go func() {
			buf := make([]byte, 1024)
			for {
				n, err := stdout.Read(buf)
				if n > 0 {
					lines := strings.Split(string(buf[:n]), "\n")
					for _, line := range lines {
						if line != "" {
							outputChan <- line
						}
					}
				}
				if err != nil {
					break
				}
			}
		}()

		// Read stderr
		go func() {
			buf := make([]byte, 1024)
			for {
				n, err := stderr.Read(buf)
				if n > 0 {
					lines := strings.Split(string(buf[:n]), "\n")
					for _, line := range lines {
						if line != "" {
							outputChan <- line
						}
					}
				}
				if err != nil {
					break
				}
			}
		}()

		// Wait for command to finish in background
		go func() {
			cmd.Wait()
			close(outputChan)
		}()

		return InteractiveStartedMsg{
			Stdin:  stdin,
			Output: outputChan,
		}
	}
}

// ListenInteractiveCmd listens for output from an interactive command
func (m TuiModel) ListenInteractiveCmd() tea.Cmd {
	outputChan := m.InteractiveOutput
	if outputChan == nil {
		return nil
	}

	return func() tea.Msg {
		line, ok := <-outputChan
		if !ok {
			return InteractiveFinishedMsg{}
		}
		return InteractiveOutputMsg{Line: line}
	}
}

// SendInteractiveInput sends input to an interactive command
func (m TuiModel) SendInteractiveInput(input string) tea.Cmd {
	stdin := m.InteractiveStdin
	if stdin == nil {
		return nil
	}

	return func() tea.Msg {
		_, err := stdin.Write([]byte(input + "\n"))
		if err != nil {
			return InteractiveFinishedMsg{Err: err}
		}
		return nil
	}
}

func (m TuiModel) SelectedServer() string {
	if len(m.Servers) == 0 || m.Selected < 0 || m.Selected >= len(m.Servers) {
		return ""
	}
	return m.Servers[m.Selected].Name
}
