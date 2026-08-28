package tui

import (
	"bufio"
	"context"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"

	tea "github.com/charmbracelet/bubbletea"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/env"
)

// ServerActionCmd runs a server action (start/stop/restart/kill) in-process
// through the API client — the unified boundary for stateful calls; only
// process orchestration (stack ops, install/reconfigure/uninstall) shells out.
func (m TuiModel) ServerActionCmd(name, action string) tea.Cmd {
	client := m.Client
	ctx := m.Ctx
	if client == nil || !m.ConfigReady {
		return func() tea.Msg {
			return ServerActionDoneMsg{Server: name, Action: action, Err: errors.New("API not connected")}
		}
	}
	return func() tea.Msg {
		if ctx == nil {
			ctx = context.Background()
		}
		uc := usecases.NewServerActionUseCase(client)
		err := uc.Execute(ctx, name, action)
		return ServerActionDoneMsg{Server: name, Action: action, Err: err}
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
		return m.StartStreamingCmd(exe, args, item.Label, item.Effect)
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
func (m TuiModel) StartStreamingCmd(exe string, args []string, label string, effect ContainerEffect) tea.Cmd {
	return func() tea.Msg {
		cmd := exec.Command(exe, args...)

		// Use combined output (stdout + stderr together)
		stdoutPipe, err := cmd.StdoutPipe()
		if err != nil {
			// The command never ran, so its container effect must not apply.
			return StreamingStartedMsg{
				Output: makeErrorChan("Failed to create pipe: " + err.Error()),
				Label:  label,
				Effect: EffectNone,
			}
		}

		// Combine stderr into stdout
		cmd.Stderr = cmd.Stdout

		if err := cmd.Start(); err != nil {
			stdoutPipe.Close()
			return StreamingStartedMsg{
				Output: makeErrorChan("Failed to start: " + err.Error()),
				Label:  label,
				Effect: EffectNone,
			}
		}

		// Create output channel
		outputChan := make(chan string, StreamingBufferSize)

		// Single goroutine to read and manage the stream
		go func() {
			defer close(outputChan)

			// Send initial status
			cmdStr := fmt.Sprintf("%s %s", filepath.Base(exe), strings.Join(args, " "))
			outputChan <- "Running: " + cmdStr
			outputChan <- "" // Blank line

			// Read output line by line
			scanner := bufio.NewScanner(stdoutPipe)
			scanner.Buffer(make([]byte, 0, 64*1024), ScannerMaxBuffer)

			lineCount := 0
			for scanner.Scan() {
				line := scanner.Text()
				outputChan <- line
				lineCount++
			}

			if err := scanner.Err(); err != nil {
				outputChan <- ""
				outputChan <- "Error reading output: " + err.Error()
			}

			// Wait for process to finish
			waitErr := cmd.Wait()
			outputChan <- ""
			if waitErr != nil {
				outputChan <- fmt.Sprintf("✗ Command failed (%d lines): %s", lineCount, waitErr.Error())
			} else {
				outputChan <- fmt.Sprintf("✓ Command completed (%d lines)", lineCount)
			}
		}()

		return StreamingStartedMsg{
			Output: outputChan,
			Label:  label,
			Effect: effect,
		}
	}
}

// makeErrorChan creates a channel with a single error message then closes
func makeErrorChan(errMsg string) <-chan string {
	ch := make(chan string, 1)
	ch <- errMsg
	close(ch)
	return ch
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

// ToggleEnvSettingCmd toggles a boolean env var between "true" and "false" in
// the .env file (via the single writer in infrastructure/env).
func (m TuiModel) ToggleEnvSettingCmd(envKey, currentValue string) tea.Cmd {
	envPath := m.Cfg.EnvPath
	newVal := "true"
	if currentValue == "true" {
		newVal = "false"
	}
	return func() tea.Msg {
		err := env.SetValue(envPath, envKey, newVal)
		return SettingsToggledMsg{Key: envKey, Val: newVal, Err: err}
	}
}

func (m TuiModel) SelectedServer() string {
	if len(m.Servers) == 0 || m.Selected < 0 || m.Selected >= len(m.Servers) {
		return ""
	}
	return m.Servers[m.Selected].Name
}
