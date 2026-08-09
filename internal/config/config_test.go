package config

import "testing"

// Covers Cloud Run's raw env var (literal \n, no godotenv unescaping).
func TestLoad_APNSAuthKey_UnescapesLiteralNewlines(t *testing.T) {
	t.Setenv("ENV", "unittest-does-not-exist")
	t.Setenv("DATABASE_URL", "postgres://example")
	t.Setenv("JWT_SECRET", "test-secret")
	t.Setenv("APNS_AUTH_KEY", `-----BEGIN PRIVATE KEY-----\nMIGTAgEAMBMGByqGSM49\n-----END PRIVATE KEY-----`)

	cfg, err := Load()
	if err != nil {
		t.Fatalf("Load() returned error: %v", err)
	}

	want := "-----BEGIN PRIVATE KEY-----\nMIGTAgEAMBMGByqGSM49\n-----END PRIVATE KEY-----"
	if cfg.APNSAuthKey != want {
		t.Fatalf("APNSAuthKey = %q, want %q", cfg.APNSAuthKey, want)
	}
}

// Covers the local-dev path, where godotenv already unescaped the value.
func TestLoad_APNSAuthKey_LeavesRealNewlinesUnchanged(t *testing.T) {
	t.Setenv("ENV", "unittest-does-not-exist")
	t.Setenv("DATABASE_URL", "postgres://example")
	t.Setenv("JWT_SECRET", "test-secret")
	real := "-----BEGIN PRIVATE KEY-----\nMIGTAgEAMBMGByqGSM49\n-----END PRIVATE KEY-----"
	t.Setenv("APNS_AUTH_KEY", real)

	cfg, err := Load()
	if err != nil {
		t.Fatalf("Load() returned error: %v", err)
	}
	if cfg.APNSAuthKey != real {
		t.Fatalf("APNSAuthKey = %q, want unchanged %q", cfg.APNSAuthKey, real)
	}
}

// NormalizePEMKey is exercised directly here too, not just through
// Load(), since cmd/jobs/plaid-sync and cmd/jobs/cycle-budgets build their
// notification config.Config by hand from os.Getenv (they can't call
// Load(), which requires DATABASE_URL/JWT_SECRET) and must call this
// helper themselves to get the same PEM-decodable value.
func TestNormalizePEMKey_UnescapesLiteralNewlines(t *testing.T) {
	in := `-----BEGIN PRIVATE KEY-----\nMIGTAgEAMBMGByqGSM49\n-----END PRIVATE KEY-----`
	want := "-----BEGIN PRIVATE KEY-----\nMIGTAgEAMBMGByqGSM49\n-----END PRIVATE KEY-----"
	if got := NormalizePEMKey(in); got != want {
		t.Fatalf("NormalizePEMKey(%q) = %q, want %q", in, got, want)
	}
}

func TestNormalizePEMKey_LeavesRealNewlinesUnchanged(t *testing.T) {
	real := "-----BEGIN PRIVATE KEY-----\nMIGTAgEAMBMGByqGSM49\n-----END PRIVATE KEY-----"
	if got := NormalizePEMKey(real); got != real {
		t.Fatalf("NormalizePEMKey(%q) = %q, want unchanged %q", real, got, real)
	}
}

func TestNormalizePEMKey_Empty(t *testing.T) {
	if got := NormalizePEMKey(""); got != "" {
		t.Fatalf("NormalizePEMKey(\"\") = %q, want empty", got)
	}
}
