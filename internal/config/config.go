package config

import (
	"fmt"
	"os"

	"github.com/joho/godotenv"
	"github.com/kelseyhightower/envconfig"
)

type Config struct {
	DatabaseURL         string `envconfig:"DATABASE_URL" required:"true"`
	JWTSecret           string `envconfig:"JWT_SECRET" required:"true"`
	JWTLifetimeSeconds  int    `envconfig:"JWT_LIFETIME_SECONDS" default:"3600"`
	GoogleClientID      string `envconfig:"GOOGLE_CLIENT_ID"`
	GoogleClientSecret  string `envconfig:"GOOGLE_CLIENT_SECRET"`
	Debug               bool   `envconfig:"DEBUG" default:"false"`
	Env                 string `envconfig:"ENV" default:"dev"`
	ServerPort          string `envconfig:"SERVER_PORT" default:"8080"`
}

func Load() (*Config, error) {
	env := os.Getenv("ENV")
	if env == "" {
		env = "dev"
	}
	envFile := fmt.Sprintf(".env.%s", env)
	// Non-fatal — production environments inject vars directly
	_ = godotenv.Load(envFile)

	var cfg Config
	if err := envconfig.Process("", &cfg); err != nil {
		return nil, fmt.Errorf("config: %w", err)
	}
	return &cfg, nil
}
