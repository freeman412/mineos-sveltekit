package env

import (
	"os"
	"path/filepath"
	"strings"
)

// This file is the single .env writer for the CLI. Every mutation goes through
// SetValue/AppendLineIfMissing so quoting, structure preservation, and the
// 0600 secret-file permission are enforced in exactly one place.

// SetValue sets key=value in the env file at path (default ".env"), preserving
// comments, blank lines, and ordering. The value is quoted when needed so it
// round-trips through godotenv. The file is created 0600 if missing, and
// always written back 0600.
func SetValue(path, key, value string) error {
	path = normalizePath(path)

	data, err := os.ReadFile(path)
	if err != nil {
		if os.IsNotExist(err) {
			return os.WriteFile(path, []byte(FormatLine(key, value)+"\n"), 0o600)
		}
		return err
	}

	lines := strings.Split(string(data), "\n")
	prefix := key + "="
	found := false
	for i, line := range lines {
		if strings.HasPrefix(strings.TrimSpace(line), prefix) {
			lines[i] = FormatLine(key, value)
			found = true
		}
	}
	if !found {
		lines = append(lines, FormatLine(key, value))
	}

	output := strings.Join(lines, "\n")
	if !strings.HasSuffix(output, "\n") {
		output += "\n"
	}
	return writeSecretFile(path, output)
}

// writeSecretFile writes content and chmods to 0600 — os.WriteFile alone
// leaves a pre-existing file's looser mode (e.g. 0644 from older installs).
func writeSecretFile(path, content string) error {
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		return err
	}
	return os.Chmod(path, 0o600)
}

// AppendLineIfMissing appends a raw line (typically a comment header) to the
// env file unless it is already present. The file must exist.
func AppendLineIfMissing(path, line string) error {
	path = normalizePath(path)

	data, err := os.ReadFile(path)
	if err != nil {
		return err
	}
	if strings.Contains(string(data), line) {
		return nil
	}
	content := string(data)
	if !strings.HasSuffix(content, "\n") {
		content += "\n"
	}
	content += "\n" + line + "\n"
	return writeSecretFile(path, content)
}

// FormatLine renders one key=value line, quoting the value when a bare write
// would be corrupted or reinterpreted on read (spaces, '#' starting a comment,
// quotes, '$' expansion, control characters).
func FormatLine(key, value string) string {
	switch {
	case !needsQuoting(value):
		return key + "=" + value
	case strings.Contains(value, "$") && !strings.Contains(value, "'") && !strings.ContainsAny(value, "\n\r"):
		// Single quotes stop godotenv's $VAR expansion, which double quotes
		// do not.
		return key + "='" + value + "'"
	default:
		escaped := strings.NewReplacer(
			`\`, `\\`,
			`"`, `\"`,
			"\n", `\n`,
			"\r", `\r`,
		).Replace(value)
		return key + `="` + escaped + `"`
	}
}

func needsQuoting(value string) bool {
	if value == "" {
		return false
	}
	if value != strings.TrimSpace(value) {
		return true
	}
	return strings.ContainsAny(value, " \t#\"'`$\n\r\\")
}

func normalizePath(path string) string {
	path = strings.TrimSpace(path)
	if path == "" {
		path = ".env"
	}
	return filepath.Clean(path)
}
