package env

import (
	"context"
	"os"

	"github.com/joho/godotenv"

	"github.com/freemancraft/mineos-sveltekit/tools/mineos-cli/internal/domain/config"
)

type DotenvRepository struct {
	path string
}

func NewDotenvRepository(path string) *DotenvRepository {
	return &DotenvRepository{path: path}
}

func (r *DotenvRepository) Load(_ context.Context) (config.Config, error) {
	cfg := config.Config{EnvPath: r.path}

	values, err := godotenv.Read(r.path)
	if err != nil {
		if os.IsNotExist(err) {
			return cfg, err
		}
		return cfg, err
	}

	cfg.ApiPort = values["API_PORT"]
	cfg.WebOrigin = values["WEB_ORIGIN_PROD"]
	if cfg.WebOrigin == "" {
		cfg.WebOrigin = values["ORIGIN"]
	}
	cfg.NetworkMode = values["MINEOS_NETWORK_MODE"]
	cfg.BuildFromSource = values["MINEOS_BUILD_FROM_SOURCE"]
	cfg.ImageTag = values["MINEOS_IMAGE_TAG"]
	cfg.ApiKeySeed = values["ApiKey__SeedKey"]
	cfg.ApiKeyStatic = values["ApiKey__StaticKey"]
	cfg.ManagementApiKey = values["MINEOS_API_KEY"]
	cfg.MinecraftHost = values["PUBLIC_MINECRAFT_HOST"]
	cfg.BodySizeLimit = values["BODY_SIZE_LIMIT"]
	cfg.DatabaseType = values["DB_TYPE"]
	cfg.DatabaseConnection = values["ConnectionStrings__DefaultConnection"]
	cfg.DataDirectory = values["Data__Directory"]
	cfg.ShutdownTimeout = values["MINEOS_SHUTDOWN_TIMEOUT"]
	cfg.PreReleaseUpdates = values["MINEOS_CLI_PRERELEASE_UPDATES"]
	cfg.TelemetryEnabled = values["MINEOS_TELEMETRY_ENABLED"]
	cfg.TelemetryEndpoint = values["MINEOS_TELEMETRY_ENDPOINT"]
	cfg.InstallationID = values["MINEOS_INSTALLATION_ID"]

	return cfg, nil
}

func (r *DotenvRepository) SetPath(path string) {
	r.path = path
}

func (r *DotenvRepository) Path() string {
	return r.path
}
