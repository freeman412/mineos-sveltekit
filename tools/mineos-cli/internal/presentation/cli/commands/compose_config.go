package commands

import (
	"os"
	"path/filepath"
	"strconv"
	"strings"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
)

func composeWithConfig(base composeRunner, cfg config.Config) composeRunner {
	result := base
	result.baseArgs = append([]string{}, base.baseArgs...)

	envPath := strings.TrimSpace(cfg.EnvPath)
	if envPath == "" {
		envPath = ".env"
	}
	envPath = filepath.Clean(envPath)
	envAbs := envPath
	if !filepath.IsAbs(envAbs) {
		if abs, err := filepath.Abs(envAbs); err == nil {
			envAbs = abs
		}
	}

	if fileExists(envAbs) {
		result.baseArgs = append(result.baseArgs, "--env-file", envAbs)
	}

	composeDir := filepath.Dir(envAbs)
	if composeDir == "." {
		composeDir = ""
	}

	files := []string{"docker-compose.yml"}
	if strings.EqualFold(strings.TrimSpace(cfg.NetworkMode), "host") {
		files = append(files, "docker-compose.host.yml")
	}
	if parseBool(cfg.BuildFromSource) {
		files = append(files, "docker-compose.build.yml")
	}

	for _, file := range files {
		path := file
		if composeDir != "" {
			path = filepath.Join(composeDir, file)
		}
		result.baseArgs = append(result.baseArgs, "-f", path)
	}

	return result
}

func parseBool(value string) bool {
	parsed, err := strconv.ParseBool(strings.TrimSpace(value))
	return err == nil && parsed
}

func fileExists(path string) bool {
	if path == "" {
		return false
	}
	info, err := os.Stat(path)
	return err == nil && !info.IsDir()
}
