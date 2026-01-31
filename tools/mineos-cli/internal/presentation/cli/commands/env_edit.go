package commands

import (
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"

	"github.com/google/uuid"
	"github.com/joho/godotenv"
)

// envDefault defines a required env var and its default value.
// If generator is set, it's called to produce the default (e.g., UUID generation).
type envDefault struct {
	key       string
	value     string
	generator func() string
	comment   string // optional comment line to add before the key
}

// requiredEnvDefaults lists env vars that must exist in .env.
// New features should add entries here so upgrades pick them up automatically.
var requiredEnvDefaults = []envDefault{
	{key: "MINEOS_SHUTDOWN_TIMEOUT", value: "300", comment: "# Server shutdown timeout (seconds)"},
	{key: "MINEOS_TELEMETRY_ENABLED", value: "true", comment: "# Telemetry"},
	{key: "MINEOS_TELEMETRY_ENDPOINT", value: "https://mineos.net"},
	{key: "MINEOS_INSTALLATION_ID", generator: func() string { return uuid.New().String() }},
	{key: "MINEOS_CLI_PRERELEASE_UPDATES", value: "false"},
}

// ensureEnvDefaults adds any missing required env vars to the .env file.
// Returns the list of keys that were added.
func ensureEnvDefaults(envPath string, out io.Writer) ([]string, error) {
	if envPath == "" {
		envPath = ".env"
	}

	values, err := loadEnvValues(envPath)
	if err != nil {
		return nil, err
	}

	var added []string
	for _, d := range requiredEnvDefaults {
		if _, exists := values[d.key]; exists {
			continue
		}

		val := d.value
		if d.generator != nil {
			val = d.generator()
		}

		if d.comment != "" {
			if err := appendLineIfMissing(envPath, d.comment); err != nil {
				return nil, err
			}
		}

		if err := setEnvFileValue(envPath, d.key, val); err != nil {
			return nil, err
		}
		added = append(added, d.key)
	}

	if len(added) > 0 && out != nil {
		fmt.Fprintf(out, "Added new configuration: %s\n", strings.Join(added, ", "))
	}
	return added, nil
}

// appendLineIfMissing appends a line to a file if it doesn't already contain it.
func appendLineIfMissing(path string, line string) error {
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
	return os.WriteFile(path, []byte(content), 0o644)
}

func loadEnvValues(path string) (map[string]string, error) {
	envPath := strings.TrimSpace(path)
	if envPath == "" {
		envPath = ".env"
	}
	return godotenv.Read(envPath)
}

func setEnvFileValue(path, key, value string) error {
	envPath := strings.TrimSpace(path)
	if envPath == "" {
		envPath = ".env"
	}
	envPath = filepath.Clean(envPath)

	data, err := os.ReadFile(envPath)
	if err != nil {
		if os.IsNotExist(err) {
			return os.WriteFile(envPath, []byte(key+"="+value+"\n"), 0o644)
		}
		return err
	}

	lines := strings.Split(string(data), "\n")
	found := false
	prefix := key + "="
	for i, line := range lines {
		trimmed := strings.TrimSpace(line)
		if strings.HasPrefix(trimmed, prefix) {
			lines[i] = prefix + value
			found = true
		}
	}
	if !found {
		lines = append(lines, prefix+value)
	}

	output := strings.Join(lines, "\n")
	if !strings.HasSuffix(output, "\n") {
		output += "\n"
	}
	return os.WriteFile(envPath, []byte(output), 0o644)
}
