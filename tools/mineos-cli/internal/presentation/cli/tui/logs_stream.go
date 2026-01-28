package tui

import (
	"bufio"
	"context"
	"fmt"
	"io"
	"os/exec"
	"sync"
	"time"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

func (m *TuiModel) EnsureLogStream() {
	if !m.LogsActive {
		return
	}

	m.StopLogs()
	ctx, cancel := context.WithCancel(context.Background())
	m.LogCancel = cancel

	if m.LogType == LogTypeDocker {
		if !m.ComposeReady {
			return
		}
		m.LogsChan, m.LogErrsChan = StreamDockerLogs(ctx, m.Compose, m.LogSource)
	} else {
		if !m.ConfigReady || m.MinecraftSource == "" {
			return
		}
		m.LogsChan, m.LogErrsChan = StreamMinecraftLogs(ctx, m.Client, m.MinecraftSource, m.MinecraftType)
	}
}

func StreamMinecraftLogs(ctx context.Context, client *api.Client, server, source string) (<-chan string, <-chan error) {
	logsChan := make(chan string, 100)
	errsChan := make(chan error, 10)

	go func() {
		defer close(logsChan)
		defer close(errsChan)

		entries, apiErrs := client.StreamConsoleLogs(ctx, server, source)
		for {
			select {
			case <-ctx.Done():
				// Normal cancellation, don't send error
				return
			case err, ok := <-apiErrs:
				if !ok {
					// Channel closed normally
					return
				}
				if err != nil {
					// Only send actual API errors, not connection closes
					if ctx.Err() == nil {
						errsChan <- err
					}
					return
				}
			case entry, ok := <-entries:
				if !ok {
					// Stream closed - will trigger reconnection via channel close
					// Don't send explicit error, let the channel close handle it
					return
				}
				msg := entry.Message
				if !entry.Timestamp.IsZero() {
					msg = fmt.Sprintf("[%s] %s", entry.Timestamp.Format(time.RFC3339), msg)
				}
				logsChan <- msg
			}
		}
	}()

	return logsChan, errsChan
}

func StreamDockerLogs(ctx context.Context, compose *ComposeRunner, service string) (<-chan string, <-chan error) {
	logs := make(chan string, 100)
	errs := make(chan error, 1)

	go func() {
		args := append([]string{}, compose.BaseArgs...)
		args = append(args, "logs", "-f", "--tail", fmt.Sprintf("%d", DefaultDockerLogTail), "--timestamps")
		if service != "" && service != DefaultDockerLogSource {
			args = append(args, service)
		}

		cmd := exec.CommandContext(ctx, compose.Exe, args...)
		stdout, err := cmd.StdoutPipe()
		if err != nil {
			errs <- err
			close(logs)
			close(errs)
			return
		}
		stderr, err := cmd.StderrPipe()
		if err != nil {
			errs <- err
			close(logs)
			close(errs)
			return
		}

		if err := cmd.Start(); err != nil {
			errs <- err
			close(logs)
			close(errs)
			return
		}

		var wg sync.WaitGroup
		readStream := func(reader io.Reader) {
			defer wg.Done()
			scanner := bufio.NewScanner(reader)
			scanner.Buffer(make([]byte, 0, 1024*1024), 1024*1024)
			for scanner.Scan() {
				select {
				case <-ctx.Done():
					return
				default:
				}
				line := scanner.Text()
				select {
				case logs <- formatDockerLogLine(line):
				case <-ctx.Done():
					return
				}
			}
		}

		wg.Add(2)
		go readStream(stdout)
		go readStream(stderr)

		// Wait for readers to finish before closing channels
		wg.Wait()
		_ = cmd.Wait()
		close(logs)
		close(errs)
	}()

	return logs, errs
}

func (m *TuiModel) StopLogs() {
	if m.LogCancel != nil {
		m.LogCancel()
		m.LogCancel = nil
	}
	m.LogsChan = nil
	m.LogErrsChan = nil
}

func formatDockerLogLine(line string) string {
	// Docker logs often have a service name prefix like "service-1 | message"
	// but it depends on the driver. For compose logs, it's usually there.
	return line // Simplified for now, styling can be added in view
}
