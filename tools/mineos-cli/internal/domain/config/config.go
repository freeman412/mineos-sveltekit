package config

type Config struct {
	EnvPath            string
	ApiPort            string
	WebOrigin          string
	NetworkMode        string
	BuildFromSource    string
	ImageTag           string
	ApiKeySeed         string
	ApiKeyStatic       string
	ManagementApiKey   string
	MinecraftHost      string
	BodySizeLimit      string
	DatabaseType       string
	DatabaseConnection string
	DataDirectory      string
	ShutdownTimeout    string
	PreReleaseUpdates  string // "true" to enable pre-release updates, "false" for stable only
	TelemetryEnabled   string // "true" to enable telemetry, "false" to disable
	TelemetryEndpoint  string // URL for telemetry endpoint
	InstallationID     string // UUID for this installation
	TelemetryKey       string // Bearer token for telemetry API
}

func (c Config) EffectiveApiKey() string {
	if c.ManagementApiKey != "" {
		return c.ManagementApiKey
	}
	if c.ApiKeyStatic != "" {
		return c.ApiKeyStatic
	}
	return c.ApiKeySeed
}

func (c Config) IsPreReleaseEnabled() bool {
	return c.PreReleaseUpdates == "true"
}

func (c Config) IsTelemetryEnabled() bool {
	// Default to true if not explicitly set to false
	return c.TelemetryEnabled != "false"
}

func (c Config) EffectiveTelemetryEndpoint() string {
	if c.TelemetryEndpoint != "" {
		return c.TelemetryEndpoint
	}
	// Default to production endpoint
	return "https://mineos.net"
}
