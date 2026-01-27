package config

type Config struct {
	EnvPath           string
	ApiPort           string
	WebOrigin         string
	NetworkMode       string
	BuildFromSource   string
	ImageTag          string
	ApiKeySeed        string
	ApiKeyStatic      string
	ManagementApiKey  string
	MinecraftHost     string
	BodySizeLimit     string
	DatabaseType      string
	DatabaseConnection string
	DataDirectory      string
	ShutdownTimeout    string
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
