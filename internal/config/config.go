package config

import (
	"fmt"
	"os"
	"time"

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
	ResendFromEmail    string   `envconfig:"RESEND_FROM_EMAIL" default:"WellSpent <noreply@wellspent.app>"`
	FrontendURL        string   `envconfig:"FRONTEND_URL" default:"http://localhost:3000"`
	PlaidClientID      string   `envconfig:"PLAID_CLIENT_ID"`
	PlaidSecret        string   `envconfig:"PLAID_SECRET"`
	PlaidEnv           string   `envconfig:"PLAID_ENV" default:"sandbox"`
	EncryptionKey      string   `envconfig:"ENCRYPTION_KEY"`

	// PlaidHTTPMaxRetries/PlaidHTTPRetryDelay configure the Plaid API HTTP
	// transport's retry-on-failure (network errors, 429, 5xx — not 4xx,
	// which won't succeed on retry).
	PlaidHTTPMaxRetries int           `envconfig:"PLAID_HTTP_MAX_RETRIES" default:"3"`
	PlaidHTTPRetryDelay time.Duration `envconfig:"PLAID_HTTP_RETRY_DELAY" default:"5s"`
	// PlaidLogRedactSensitive scrubs client_id/secret/access_token/
	// public_token/link_token from logged Plaid request/response bodies.
	// Defaults to true — these are bank-account credentials. The
	// PLAID-CLIENT-ID/PLAID-SECRET headers are always redacted regardless.
	PlaidLogRedactSensitive bool `envconfig:"PLAID_LOG_REDACT_SENSITIVE" default:"true"`

	// RateLimitRPS/RateLimitBurst configure the per-IP token-bucket applied
	// to every incoming request, ahead of CORS and routing. Defaults are
	// generous enough for a single browser session's burst of RPCs on page
	// load (10+ concurrent list calls) while still bounding flood/scan traffic.
	RateLimitRPS   float64 `envconfig:"RATE_LIMIT_RPS" default:"10"`
	RateLimitBurst int     `envconfig:"RATE_LIMIT_BURST" default:"30"`
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
