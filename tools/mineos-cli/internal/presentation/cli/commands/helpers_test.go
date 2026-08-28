package commands

import (
	"fmt"
	"runtime"
	"strings"
	"testing"

	"github.com/spf13/cobra"
)

func TestIsValidRelativePath(t *testing.T) {
	valid := []string{"mineos", "sub/dir", "./here", "a.b"}
	invalid := []string{"", "  ", "/abs", "~home", "..", "../up", "sub/../../out", "\\win"}
	for _, p := range valid {
		if !isValidRelativePath(p) {
			t.Fatalf("%q should be valid", p)
		}
	}
	for _, p := range invalid {
		if isValidRelativePath(p) {
			t.Fatalf("%q should be invalid", p)
		}
	}
}

func TestParseEnvInt(t *testing.T) {
	cases := []struct {
		raw  string
		want int
	}{
		{"", 42}, {"  ", 42}, {"7", 7}, {" 7 ", 7}, {"x", 42}, {"-3", -3},
	}
	for _, c := range cases {
		if got := parseEnvInt(c.raw, 42); got != c.want {
			t.Fatalf("parseEnvInt(%q) = %d, want %d", c.raw, got, c.want)
		}
	}
}

func TestParseEnvBool(t *testing.T) {
	for _, truthy := range []string{"true", "TRUE", "1", " t "} {
		if !parseEnvBool(truthy) {
			t.Fatalf("%q should parse true", truthy)
		}
	}
	for _, falsy := range []string{"", "false", "0", "yes", "junk"} {
		if parseEnvBool(falsy) {
			t.Fatalf("%q should parse false", falsy)
		}
	}
}

func TestIsPreviewTag(t *testing.T) {
	preview := []string{"preview", "Preview", "v1.2.0-beta.4", "v2.0.0-alpha.1", "v1.0.0-rc.2"}
	stable := []string{"latest", "v1.2.0", "stable", ""}
	for _, tag := range preview {
		if !isPreviewTag(tag) {
			t.Fatalf("%q should be preview", tag)
		}
	}
	for _, tag := range stable {
		if isPreviewTag(tag) {
			t.Fatalf("%q should be stable", tag)
		}
	}
}

func TestGetAssetName(t *testing.T) {
	want := fmt.Sprintf("mineos-cli_%s_%s.zip", runtime.GOOS, runtime.GOARCH)
	if got := getAssetName(); got != want {
		t.Fatalf("got %q, want %q", got, want)
	}
	if !strings.HasSuffix(getAssetName(), ".zip") {
		t.Fatal("asset must be a zip")
	}
}

func TestResolveUninstallMode_ExplicitModes(t *testing.T) {
	cmd := &cobra.Command{}
	cases := map[string]string{
		"1": "containers", "containers": "containers", "container": "containers", "keep": "containers",
		"2": "backup", "backup": "backup",
		"3": "remove", "remove": "remove", "delete": "remove",
		"4": "complete", "complete": "complete", "full": "complete", "everything": "complete",
		" Complete ": "complete", "KEEP": "containers",
	}
	for in, want := range cases {
		got, err := resolveUninstallMode(cmd, in)
		if err != nil || got != want {
			t.Fatalf("resolveUninstallMode(%q) = %q, %v; want %q", in, got, err, want)
		}
	}
	if _, err := resolveUninstallMode(cmd, "5"); err == nil {
		t.Fatal("invalid mode must error")
	}
	if _, err := resolveUninstallMode(cmd, "nuke"); err == nil {
		t.Fatal("invalid mode must error")
	}
}
