package env

import (
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/joho/godotenv"
)

func writeFixture(t *testing.T, content string) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), ".env")
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}
	return path
}

func TestSetValue_UpdatesInPlacePreservingStructure(t *testing.T) {
	path := writeFixture(t, "# MineOS config\nAPI_PORT=5078\n\n# Keys\nMINEOS_API_KEY=old\n")

	if err := SetValue(path, "MINEOS_API_KEY", "new"); err != nil {
		t.Fatal(err)
	}

	data, _ := os.ReadFile(path)
	content := string(data)
	if !strings.Contains(content, "# MineOS config") || !strings.Contains(content, "# Keys") {
		t.Fatalf("comments lost:\n%s", content)
	}
	if !strings.Contains(content, "MINEOS_API_KEY=new") || strings.Contains(content, "old") {
		t.Fatalf("value not replaced:\n%s", content)
	}
	if strings.Index(content, "API_PORT") > strings.Index(content, "MINEOS_API_KEY") {
		t.Fatalf("ordering changed:\n%s", content)
	}
}

func TestSetValue_AppendsMissingKey(t *testing.T) {
	path := writeFixture(t, "API_PORT=5078\n")
	if err := SetValue(path, "NEW_KEY", "val"); err != nil {
		t.Fatal(err)
	}
	values, err := godotenv.Read(path)
	if err != nil {
		t.Fatal(err)
	}
	if values["NEW_KEY"] != "val" || values["API_PORT"] != "5078" {
		t.Fatalf("append failed: %v", values)
	}
}

func TestSetValue_CreatesMissingFileWith0600(t *testing.T) {
	path := filepath.Join(t.TempDir(), ".env")
	if err := SetValue(path, "KEY", "val"); err != nil {
		t.Fatal(err)
	}
	info, err := os.Stat(path)
	if err != nil {
		t.Fatal(err)
	}
	if info.Mode().Perm() != 0o600 {
		t.Fatalf("want 0600, got %o", info.Mode().Perm())
	}
}

func TestSetValue_TightensPermissionsOnRewrite(t *testing.T) {
	path := writeFixture(t, "KEY=old\n") // fixture is written 0644
	if err := SetValue(path, "KEY", "new"); err != nil {
		t.Fatal(err)
	}
	info, _ := os.Stat(path)
	if info.Mode().Perm() != 0o600 {
		t.Fatalf("want 0600 after rewrite, got %o", info.Mode().Perm())
	}
}

// The bug that motivated #134: values with '#', spaces, quotes, or '$' were
// written bare by the hand-rolled writers and then silently corrupted on read.
func TestSetValue_RoundTripsTrickyValues(t *testing.T) {
	tricky := []string{
		"plain",
		"has spaces here",
		"trailing#comment",
		"quote\"inside",
		"single'quote",
		"dollar$sign",
		"${looks_like_var}",
		"back\\slash",
		"semi;colon",
	}
	for _, want := range tricky {
		path := writeFixture(t, "OTHER=1\n")
		if err := SetValue(path, "KEY", want); err != nil {
			t.Fatalf("%q: %v", want, err)
		}
		values, err := godotenv.Read(path)
		if err != nil {
			t.Fatalf("%q: read failed: %v", want, err)
		}
		if got := values["KEY"]; got != want {
			t.Fatalf("round-trip corrupted %q -> %q", want, got)
		}
		if values["OTHER"] != "1" {
			t.Fatalf("%q: sibling key corrupted", want)
		}
	}
}

func TestSetValue_ReplacesQuotedExistingValue(t *testing.T) {
	path := writeFixture(t, `KEY="old value"`+"\n")
	if err := SetValue(path, "KEY", "new"); err != nil {
		t.Fatal(err)
	}
	values, _ := godotenv.Read(path)
	if values["KEY"] != "new" {
		t.Fatalf("quoted value not replaced: %v", values)
	}
}

func TestAppendLineIfMissing(t *testing.T) {
	path := writeFixture(t, "KEY=1\n")

	if err := AppendLineIfMissing(path, "# Section"); err != nil {
		t.Fatal(err)
	}
	if err := AppendLineIfMissing(path, "# Section"); err != nil {
		t.Fatal(err)
	}
	data, _ := os.ReadFile(path)
	if strings.Count(string(data), "# Section") != 1 {
		t.Fatalf("comment duplicated:\n%s", data)
	}

	if err := AppendLineIfMissing(filepath.Join(t.TempDir(), "missing"), "x"); err == nil {
		t.Fatal("missing file must error")
	}
}

func TestFormatLine(t *testing.T) {
	cases := map[string]string{
		"plain":       "K=plain",
		"":            "K=",
		"with space":  `K="with space"`,
		"a#b":         `K="a#b"`,
		"pa$$word":    "K='pa$$word'",
		"d'oh $1":     `K="d'oh $1"`, // single quote inside forces double quotes
		"line\nbreak": `K="line\nbreak"`,
	}
	for value, want := range cases {
		if got := FormatLine("K", value); got != want {
			t.Fatalf("FormatLine(%q) = %q, want %q", value, got, want)
		}
	}
}
