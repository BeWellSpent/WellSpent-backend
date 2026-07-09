package config

import (
	"fmt"
	"os"

	"github.com/joho/godotenv"
	"github.com/kelseyhightower/envconfig"
)

type Config struct {
	DatabaseURL        string   `envconfig:"DATABASE_URL" required:"true"`
	JWTSecret          string   `envconfig:"JWT_SECRET" required:"true"`
	GoogleClientID     string   `envconfig:"GOOGLE_CLIENT_ID"`
	GoogleClientSecret string   `envconfig:"GOOGLE_CLIENT_SECRET"`
	GoogleRedirectURI  string   `envconfig:"GOOGLE_REDIRECT_URI"`
	CORSAllowedOrigins []string `envconfig:"CORS_ALLOWED_ORIGINS" default:"http://localhost:5173,http://localhost:3000"`
	Debug              bool     `envconfig:"DEBUG" default:"false"`
	Env                string   `envconfig:"ENV" default:"dev"`
	ServerPort         string   `envconfig:"PORT" default:"8080"`
	ResendAPIKey       string   `envconfig:"RESEND_API_KEY"`
	ResendFromEmail    string   `envconfig:"RESEND_FROM_EMAIL" default:"WellSpent <noreply@spendsense.app>"`
	FrontendURL        string   `envconfig:"FRONTEND_URL" default:"http://localhost:3000"`
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
