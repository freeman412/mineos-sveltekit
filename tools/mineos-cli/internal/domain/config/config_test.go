package config

import "testing"

func TestEffectiveApiKey_Precedence(t *testing.T) {
	cases := []struct {
		name string
		cfg  Config
		want string
	}{
		{"management wins", Config{ManagementApiKey: "m", ApiKeyStatic: "s", ApiKeySeed: "d"}, "m"},
		{"static over seed", Config{ApiKeyStatic: "s", ApiKeySeed: "d"}, "s"},
		{"seed as fallback", Config{ApiKeySeed: "d"}, "d"},
		{"all empty", Config{}, ""},
	}
	for _, c := range cases {
		if got := c.cfg.EffectiveApiKey(); got != c.want {
			t.Fatalf("%s: got %q, want %q", c.name, got, c.want)
		}
	}
}

func TestIsPreReleaseEnabled(t *testing.T) {
	if (Config{PreReleaseUpdates: "true"}).IsPreReleaseEnabled() != true {
		t.Fatal("'true' must enable pre-release")
	}
	for _, v := range []string{"", "false", "TRUE", "1", "yes"} {
		if (Config{PreReleaseUpdates: v}).IsPreReleaseEnabled() {
			t.Fatalf("%q must not enable pre-release (exact 'true' only)", v)
		}
	}
}

func TestIsTelemetryEnabled_DefaultsOn(t *testing.T) {
	if !(Config{}).IsTelemetryEnabled() {
		t.Fatal("unset telemetry must default to enabled")
	}
	if (Config{TelemetryEnabled: "false"}).IsTelemetryEnabled() {
		t.Fatal("'false' must disable telemetry")
	}
	if !(Config{TelemetryEnabled: "anything-else"}).IsTelemetryEnabled() {
		t.Fatal("only exact 'false' disables telemetry")
	}
}

func TestEffectiveTelemetryEndpoint(t *testing.T) {
	if got := (Config{}).EffectiveTelemetryEndpoint(); got != "https://mineos.net" {
		t.Fatalf("default endpoint wrong: %q", got)
	}
	if got := (Config{TelemetryEndpoint: "http://localhost:9"}).EffectiveTelemetryEndpoint(); got != "http://localhost:9" {
		t.Fatalf("override ignored: %q", got)
	}
}
