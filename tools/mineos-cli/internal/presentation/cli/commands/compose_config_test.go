package commands

import (
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
)

func writeEnvFixture(t *testing.T) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), ".env")
	if err := os.WriteFile(path, []byte("API_PORT=5078\n"), 0o600); err != nil {
		t.Fatal(err)
	}
	return path
}

func argsString(r composeRunner) string {
	return strings.Join(r.baseArgs, " ")
}

func TestComposeWithConfig_DefaultBridgeMode(t *testing.T) {
	envPath := writeEnvFixture(t)
	base := composeRunner{exe: "docker", baseArgs: []string{"compose"}}

	got := composeWithConfig(base, config.Config{EnvPath: envPath})
	args := argsString(got)

	if !strings.Contains(args, "--env-file "+envPath) {
		t.Fatalf("env file not wired: %s", args)
	}
	if !strings.Contains(args, "docker-compose.yml") {
		t.Fatalf("base compose file missing: %s", args)
	}
	if strings.Contains(args, "host.yml") || strings.Contains(args, "build.yml") {
		t.Fatalf("unexpected overlay files: %s", args)
	}
}

func TestComposeWithConfig_HostModeAndBuildOverlays(t *testing.T) {
	envPath := writeEnvFixture(t)
	base := composeRunner{exe: "docker", baseArgs: []string{"compose"}}

	got := composeWithConfig(base, config.Config{
		EnvPath:         envPath,
		NetworkMode:     "HOST", // case-insensitive
		BuildFromSource: "true",
	})
	args := argsString(got)

	for _, want := range []string{"docker-compose.yml", "docker-compose.host.yml", "docker-compose.build.yml"} {
		if !strings.Contains(args, want) {
			t.Fatalf("missing %s in: %s", want, args)
		}
	}
	// Overlays must come after the base file (compose merge order matters).
	if strings.Index(args, "docker-compose.host.yml") < strings.Index(args, "-f docker-compose.yml")+2 &&
		strings.Index(args, "docker-compose.yml") > strings.Index(args, "docker-compose.host.yml") {
		t.Fatalf("overlay ordering wrong: %s", args)
	}
}

func TestComposeWithConfig_MissingEnvFileSkipsFlag(t *testing.T) {
	base := composeRunner{exe: "docker", baseArgs: []string{"compose"}}
	got := composeWithConfig(base, config.Config{EnvPath: filepath.Join(t.TempDir(), "nope.env")})
	if strings.Contains(argsString(got), "--env-file") {
		t.Fatalf("nonexistent env file must not be passed: %s", argsString(got))
	}
}

func TestComposeWithConfig_DoesNotMutateBase(t *testing.T) {
	base := composeRunner{exe: "docker", baseArgs: []string{"compose"}}
	_ = composeWithConfig(base, config.Config{EnvPath: writeEnvFixture(t), BuildFromSource: "true"})
	if len(base.baseArgs) != 1 || base.baseArgs[0] != "compose" {
		t.Fatalf("base runner mutated: %v", base.baseArgs)
	}
}
