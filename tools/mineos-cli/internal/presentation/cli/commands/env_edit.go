package commands

import (
	"os"
	"path/filepath"
	"strings"

	"github.com/joho/godotenv"
)

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
