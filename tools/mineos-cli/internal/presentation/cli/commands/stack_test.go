package commands

import (
	"bytes"
	"context"
	"path/filepath"
	"testing"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/application/usecases"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/infrastructure/env"
)

// fakeRunner records compose invocations instead of exec'ing docker.
type fakeRunner struct {
	calls [][]string
	err   error
}

func (f *fakeRunner) run(args []string) error {
	f.calls = append(f.calls, args)
	return f.err
}

// unreachableLoadConfig returns a use case whose API is unreachable, so the
// stop-servers step degrades to a warning and compose is still invoked.
func unreachableLoadConfig(t *testing.T) *usecases.LoadConfigUseCase {
	t.Helper()
	repo := env.NewDotenvRepository(filepath.Join(t.TempDir(), "missing.env"))
	return usecases.NewLoadConfigUseCase(repo)
}

func TestGracefulStop_ForceStopsImmediately(t *testing.T) {
	fake := &fakeRunner{}
	var out bytes.Buffer

	err := gracefulStop(context.Background(), unreachableLoadConfig(t), fake, config.Config{}, 60, true, &out)
	if err != nil {
		t.Fatal(err)
	}
	if len(fake.calls) != 1 {
		t.Fatalf("want 1 compose call, got %v", fake.calls)
	}
	got := fake.calls[0]
	if got[0] != "stop" || got[1] != "-t" || got[2] != "0" {
		t.Fatalf("force stop must use -t 0, got %v", got)
	}
	if !bytes.Contains(out.Bytes(), []byte("Warning:")) {
		t.Fatal("unreachable API must surface as a warning, not an error")
	}
}

func TestGracefulStop_GracefulUsesShortDockerTimeout(t *testing.T) {
	fake := &fakeRunner{}
	var out bytes.Buffer

	err := gracefulStop(context.Background(), unreachableLoadConfig(t), fake, config.Config{}, 120, false, &out)
	if err != nil {
		t.Fatal(err)
	}
	if len(fake.calls) != 1 {
		t.Fatalf("want 1 compose call, got %v", fake.calls)
	}
	got := fake.calls[0]
	// Minecraft servers are stopped via the API; containers get a short SIGTERM window.
	if got[0] != "stop" || got[1] != "-t" || got[2] != "30" {
		t.Fatalf("graceful stop must use -t 30, got %v", got)
	}
}

func TestGracefulStop_ComposeErrorPropagates(t *testing.T) {
	fake := &fakeRunner{err: errBoom}
	var out bytes.Buffer

	if err := gracefulStop(context.Background(), unreachableLoadConfig(t), fake, config.Config{}, 60, true, &out); err == nil {
		t.Fatal("compose failure must propagate")
	}
}

var errBoom = &boomError{}

type boomError struct{}

func (*boomError) Error() string { return "boom" }
