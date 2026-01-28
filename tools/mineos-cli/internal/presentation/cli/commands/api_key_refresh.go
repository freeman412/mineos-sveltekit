package commands

import (
	"context"
	"database/sql"
	"errors"
	"fmt"
	"path/filepath"
	"strings"
	"time"

	_ "modernc.org/sqlite"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
)

var errNoApiKeyFound = errors.New("no active API key found in database")

func refreshApiKeyFromDb(cfg config.Config) (string, error) {
	if !isSqliteConfig(cfg) {
		return "", errors.New("API key refresh is only supported for sqlite installations")
	}

	envPath := resolveEnvPath(cfg.EnvPath)
	dataDir := resolveDataDir(cfg, envPath)
	dbPath, err := resolveSqliteDbPath(cfg, dataDir)
	if err != nil {
		return "", err
	}

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	db, err := sql.Open("sqlite", dbPath)
	if err != nil {
		return "", err
	}
	defer db.Close()

	var key string
	row := db.QueryRowContext(ctx, "SELECT Key FROM ApiKeys WHERE Revoked=0 ORDER BY CreatedAt DESC LIMIT 1")
	if err := row.Scan(&key); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			return "", errNoApiKeyFound
		}
		return "", err
	}

	key = strings.TrimSpace(key)
	if key == "" {
		return "", errNoApiKeyFound
	}

	if err := setEnvFileValue(envPath, "MINEOS_API_KEY", key); err != nil {
		return "", err
	}

	return key, nil
}

func isSqliteConfig(cfg config.Config) bool {
	dbType := strings.TrimSpace(cfg.DatabaseType)
	if dbType == "" {
		return true
	}
	return strings.EqualFold(dbType, "sqlite")
}

func resolveEnvPath(path string) string {
	envPath := strings.TrimSpace(path)
	if envPath == "" {
		envPath = ".env"
	}
	envPath = filepath.Clean(envPath)
	if filepath.IsAbs(envPath) {
		return envPath
	}
	abs, err := filepath.Abs(envPath)
	if err != nil {
		return envPath
	}
	return abs
}

func resolveDataDir(cfg config.Config, envPath string) string {
	dataDir := strings.TrimSpace(cfg.DataDirectory)
	if dataDir == "" {
		dataDir = "./data"
	}
	if filepath.IsAbs(dataDir) {
		return filepath.Clean(dataDir)
	}
	envDir := filepath.Dir(envPath)
	return filepath.Clean(filepath.Join(envDir, dataDir))
}

func resolveSqliteDbPath(cfg config.Config, dataDir string) (string, error) {
	dbPath := filepath.Join(dataDir, "mineos.db")
	if source := parseDataSource(cfg.DatabaseConnection); source != "" {
		switch {
		case strings.HasPrefix(source, "/app/data/"):
			dbPath = filepath.Join(dataDir, filepath.Base(source))
		case filepath.IsAbs(source):
			dbPath = source
		default:
			dbPath = filepath.Join(dataDir, source)
		}
	}

	if !fileExists(dbPath) {
		return "", fmt.Errorf("sqlite database not found at %s", dbPath)
	}
	return dbPath, nil
}

func parseDataSource(conn string) string {
	for _, part := range strings.Split(conn, ";") {
		part = strings.TrimSpace(part)
		if part == "" {
			continue
		}
		key, value, found := strings.Cut(part, "=")
		if !found {
			continue
		}
		switch strings.ToLower(strings.TrimSpace(key)) {
		case "data source", "datasource":
			return strings.TrimSpace(value)
		}
	}
	return ""
}
