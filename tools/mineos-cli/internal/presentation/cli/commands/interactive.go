package commands

import (
	"bufio"
	"context"
	"fmt"
	"io"
	"os"
	"os/signal"
	"sort"
	"strconv"
	"strings"
	"time"

	"github.com/spf13/cobra"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/api"
)

const (
	defaultLogSource = "combined"
)

type interactiveSession struct {
	loadConfig    *usecases.LoadConfigUseCase
	out           io.Writer
	currentServer string
	logSource     string
}

func NewInteractiveCommand(loadConfig *usecases.LoadConfigUseCase) *cobra.Command {
	return &cobra.Command{
		Use:     "interactive",
		Aliases: []string{"shell", "repl"},
		Short:   "Interactive MineOS shell",
		RunE: func(cmd *cobra.Command, _ []string) error {
			session := &interactiveSession{
				loadConfig: loadConfig,
				out:        cmd.OutOrStdout(),
				logSource:  defaultLogSource,
			}
			return session.Run(cmd.Context(), os.Stdin)
		},
	}
}

func (s *interactiveSession) Run(ctx context.Context, in io.Reader) error {
	fmt.Fprintln(s.out, "MineOS interactive shell")
	fmt.Fprintln(s.out, "Type 'help' for commands. Ctrl+C exits logs; 'quit' exits the shell.")

	scanner := bufio.NewScanner(in)
	scanner.Buffer(make([]byte, 0, 64*1024), 1024*1024)

	for {
		fmt.Fprint(s.out, s.prompt())
		if !scanner.Scan() {
			if err := scanner.Err(); err != nil {
				return err
			}
			fmt.Fprintln(s.out, "\nGoodbye.")
			return nil
		}

		line := strings.TrimSpace(scanner.Text())
		if line == "" {
			continue
		}
		quit, err := s.handleLine(ctx, line)
		if err != nil {
			fmt.Fprintf(s.out, "error: %v\n", err)
		}
		if quit {
			return nil
		}
	}
}

func (s *interactiveSession) handleLine(ctx context.Context, line string) (bool, error) {
	fields := strings.Fields(line)
	if len(fields) == 0 {
		return false, nil
	}

	command := strings.ToLower(fields[0])
	switch command {
	case "help", "?":
		s.printHelp()
		return false, nil
	case "quit", "exit", "q":
		fmt.Fprintln(s.out, "Goodbye.")
		return true, nil
	case "list", "ls", "servers":
		return false, s.listServers(ctx)
	case "use", "select":
		if len(fields) < 2 {
			return false, fmt.Errorf("usage: use <server>")
		}
		s.currentServer = fields[1]
		fmt.Fprintf(s.out, "Selected server: %s\n", s.currentServer)
		return false, nil
	case "status":
		return false, s.showStatus(ctx)
	case "health":
		return false, s.checkHealth(ctx)
	case "stop-all", "stopall":
		timeout := 300
		if len(fields) >= 2 {
			value, err := strconv.Atoi(fields[1])
			if err != nil {
				return false, fmt.Errorf("timeout must be a number")
			}
			timeout = value
		}
		return false, s.stopAll(ctx, timeout)
	case "logs", "log":
		return false, s.handleLogs(ctx, fields)
	case "console", "cmd":
		return false, s.handleConsoleCommand(ctx, line, fields[0])
	case "start", "stop", "restart", "kill":
		return false, s.handleServerAction(ctx, command, fields)
	default:
		return false, fmt.Errorf("unknown command: %s (type 'help')", fields[0])
	}
}

func (s *interactiveSession) prompt() string {
	if s.currentServer == "" {
		return "mineos> "
	}
	return fmt.Sprintf("mineos(%s)> ", s.currentServer)
}

func (s *interactiveSession) printHelp() {
	fmt.Fprintln(s.out, "Commands:")
	fmt.Fprintln(s.out, "  list | ls                 List servers")
	fmt.Fprintln(s.out, "  use <server>              Select a server")
	fmt.Fprintln(s.out, "  start|stop|restart|kill    Control the selected server (or pass a name)")
	fmt.Fprintln(s.out, "  stop-all [timeout]         Stop all servers (default 300s)")
	fmt.Fprintln(s.out, "  logs [server] [source]     Stream logs (source: combined|server|java|crash)")
	fmt.Fprintln(s.out, "  console <command>          Send a console command to the selected server")
	fmt.Fprintln(s.out, "  status                     Show API + config status")
	fmt.Fprintln(s.out, "  health                     Check API health")
	fmt.Fprintln(s.out, "  quit | exit | q            Exit the shell")
}

func (s *interactiveSession) handleServerAction(ctx context.Context, action string, fields []string) error {
	server, err := s.resolveServer(fields)
	if err != nil {
		return err
	}

	cfg, err := s.loadConfig.Execute(ctx)
	if err != nil {
		return err
	}
	client := api.NewClientFromConfig(cfg)
	uc := usecases.NewServerActionUseCase(client)
	if err := uc.Execute(ctx, server, action); err != nil {
		return err
	}
	fmt.Fprintf(s.out, "%s: %s\n", action, server)
	return nil
}

func (s *interactiveSession) handleLogs(ctx context.Context, fields []string) error {
	server := s.currentServer
	if len(fields) >= 2 {
		server = fields[1]
	}
	if server == "" {
		return fmt.Errorf("select a server with 'use <name>' or provide one to logs")
	}

	source := s.logSource
	if len(fields) >= 3 {
		source = fields[2]
	}
	if source == "" {
		source = defaultLogSource
	}
	s.logSource = source

	cfg, err := s.loadConfig.Execute(ctx)
	if err != nil {
		return err
	}
	client := api.NewClientFromConfig(cfg)

	fmt.Fprintf(s.out, "Streaming logs for %s (%s). Press Ctrl+C to return.\n", server, source)
	streamCtx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()

	logs, errs := client.StreamConsoleLogs(streamCtx, server, source)
	for {
		select {
		case entry, ok := <-logs:
			if !ok {
				return nil
			}
			s.printLogEntry(entry)
		case err, ok := <-errs:
			if ok && err != nil {
				return err
			}
		case <-streamCtx.Done():
			fmt.Fprintln(s.out, "Log stream stopped.")
			return nil
		}
	}
}

func (s *interactiveSession) handleConsoleCommand(ctx context.Context, line, prefix string) error {
	command := strings.TrimSpace(line[len(prefix):])
	if command == "" {
		return fmt.Errorf("usage: console <command>")
	}

	server := s.currentServer
	if server == "" {
		return fmt.Errorf("select a server with 'use <name>' before sending console commands")
	}

	cfg, err := s.loadConfig.Execute(ctx)
	if err != nil {
		return err
	}
	client := api.NewClientFromConfig(cfg)
	if err := client.SendConsoleCommand(ctx, server, command); err != nil {
		return err
	}
	fmt.Fprintf(s.out, "sent to %s: %s\n", server, command)
	return nil
}

func (s *interactiveSession) listServers(ctx context.Context) error {
	cfg, err := s.loadConfig.Execute(ctx)
	if err != nil {
		return err
	}
	client := api.NewClientFromConfig(cfg)
	uc := usecases.NewListServersUseCase(client)
	servers, err := uc.Execute(ctx)
	if err != nil {
		return err
	}
	if len(servers) == 0 {
		fmt.Fprintln(s.out, "No servers found.")
		return nil
	}
	sort.Slice(servers, func(i, j int) bool { return servers[i].Name < servers[j].Name })
	for _, server := range servers {
		prefix := " "
		if server.Name == s.currentServer {
			prefix = "*"
		}
		fmt.Fprintf(s.out, "%s %s\t%s\n", prefix, server.Name, server.Status)
	}
	return nil
}

func (s *interactiveSession) stopAll(ctx context.Context, timeout int) error {
	cfg, err := s.loadConfig.Execute(ctx)
	if err != nil {
		return err
	}
	client := api.NewClientFromConfig(cfg)
	uc := usecases.NewStopAllServersUseCase(client)
	result, err := uc.Execute(ctx, timeout)
	if err != nil {
		return err
	}
	fmt.Fprintf(s.out, "Total: %d, running: %d, stopped: %d, skipped: %d\n", result.Total, result.Running, result.Stopped, result.Skipped)
	for _, item := range result.Results {
		if item.Error != "" {
			fmt.Fprintf(s.out, "%s\t%s\t%s\n", item.Name, item.Status, item.Error)
		} else {
			fmt.Fprintf(s.out, "%s\t%s\n", item.Name, item.Status)
		}
	}
	return nil
}

func (s *interactiveSession) showStatus(ctx context.Context) error {
	cfg, err := s.loadConfig.Execute(ctx)
	if err != nil {
		return err
	}
	client := api.NewClientFromConfig(cfg)
	health := "unhealthy"
	if err := client.Health(ctx); err == nil {
		health = "healthy"
	}
	fmt.Fprintf(s.out, "API: %s\n", health)
	fmt.Fprintf(s.out, "Web origin: %s\n", fallback(cfg.WebOrigin, "http://localhost:3000"))
	fmt.Fprintf(s.out, "Minecraft host: %s\n", fallback(cfg.MinecraftHost, "localhost"))
	fmt.Fprintf(s.out, "Network mode: %s\n", fallback(cfg.NetworkMode, "bridge"))
	return nil
}

func (s *interactiveSession) checkHealth(ctx context.Context) error {
	cfg, err := s.loadConfig.Execute(ctx)
	if err != nil {
		return err
	}
	client := api.NewClientFromConfig(cfg)
	uc := usecases.NewHealthCheckUseCase(client)
	if err := uc.Execute(ctx); err != nil {
		return err
	}
	fmt.Fprintln(s.out, "API: healthy")
	return nil
}

func (s *interactiveSession) resolveServer(fields []string) (string, error) {
	if len(fields) >= 2 {
		return fields[1], nil
	}
	if s.currentServer == "" {
		return "", fmt.Errorf("select a server with 'use <name>' or pass a name")
	}
	return s.currentServer, nil
}

func (s *interactiveSession) printLogEntry(entry api.LogEntry) {
	timestamp := entry.Timestamp
	if timestamp.IsZero() {
		fmt.Fprintln(s.out, entry.Message)
		return
	}
	fmt.Fprintf(s.out, "[%s] %s\n", timestamp.Format(time.RFC3339), entry.Message)
}
