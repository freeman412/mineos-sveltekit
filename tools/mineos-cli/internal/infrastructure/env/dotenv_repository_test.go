package env

import (
	"context"
	"os"
	"path/filepath"
	"testing"
)

func TestLoad_MissingFileErrorsButKeepsPath(t *testing.T) {
	path := filepath.Join(t.TempDir(), ".env")
	cfg, err := NewDotenvRepository(path).Load(context.Background())
	if !os.IsNotExist(err) {
		t.Fatalf("want not-exist error, got %v", err)
	}
	if cfg.EnvPath != path {
		t.Fatal("EnvPath must be set even on error")
	}
}

func TestLoad_MapsAllKeys(t *testing.T) {
	content := `API_PORT=5078
WEB_ORIGIN_PROD=https://example.com
MINEOS_NETWORK_MODE=host
MINEOS_BUILD_FROM_SOURCE=true
MINEOS_IMAGE_TAG=preview
ApiKey__SeedKey=seed
ApiKey__StaticKey=static
MINEOS_API_KEY=mgmt
PUBLIC_MINECRAFT_HOST=mc.example.com
BODY_SIZE_LIMIT=512M
DB_TYPE=postgres
ConnectionStrings__DefaultConnection=Host=db
Data__Directory=/data
MINEOS_SHUTDOWN_TIMEOUT=300
MINEOS_CLI_PRERELEASE_UPDATES=true
MINEOS_TELEMETRY_ENABLED=false
MINEOS_TELEMETRY_ENDPOINT=http://t
MINEOS_INSTALLATION_ID=uuid-1
MINEOS_TELEMETRY_KEY=tk
`
	path := writeFixture(t, content)
	cfg, err := NewDotenvRepository(path).Load(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	checks := map[string]string{
		"ApiPort":            cfg.ApiPort,
		"WebOrigin":          cfg.WebOrigin,
		"NetworkMode":        cfg.NetworkMode,
		"ManagementApiKey":   cfg.ManagementApiKey,
		"DatabaseConnection": cfg.DatabaseConnection,
		"InstallationID":     cfg.InstallationID,
	}
	wants := map[string]string{
		"ApiPort":            "5078",
		"WebOrigin":          "https://example.com",
		"NetworkMode":        "host",
		"ManagementApiKey":   "mgmt",
		"DatabaseConnection": "Host=db",
		"InstallationID":     "uuid-1",
	}
	for field, got := range checks {
		if got != wants[field] {
			t.Fatalf("%s = %q, want %q", field, got, wants[field])
		}
	}
	if !cfg.IsPreReleaseEnabled() || cfg.IsTelemetryEnabled() {
		t.Fatal("boolean-ish fields not mapped")
	}
}

func TestLoad_WebOriginFallsBackToOrigin(t *testing.T) {
	path := writeFixture(t, "ORIGIN=http://fallback\n")
	cfg, err := NewDotenvRepository(path).Load(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if cfg.WebOrigin != "http://fallback" {
		t.Fatalf("ORIGIN fallback not applied: %q", cfg.WebOrigin)
	}

	path = writeFixture(t, "WEB_ORIGIN_PROD=http://primary\nORIGIN=http://fallback\n")
	cfg, _ = NewDotenvRepository(path).Load(context.Background())
	if cfg.WebOrigin != "http://primary" {
		t.Fatalf("WEB_ORIGIN_PROD must win: %q", cfg.WebOrigin)
	}
}
