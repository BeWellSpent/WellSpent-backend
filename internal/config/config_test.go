package config

import "testing"

// TestLoad_APNSAuthKey_UnescapesLiteralNewlines covers the exact production
// scenario: Cloud Run sets APNS_AUTH_KEY as a raw process env var (no
// godotenv involved, since there's no .env file in the container), stored
// with literal \n escapes standing in for the PEM file's real line breaks.
// Without unescaping, apnstoken.AuthKeyFromBytes fails to parse the key and
// every push silently no-ops.
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

// TestLoad_APNSAuthKey_LeavesRealNewlinesUnchanged covers the local-dev path,
// where godotenv has already unescaped a quoted .env value into a string
// with genuine newline bytes before envconfig ever sees it — re-applying the
// same ReplaceAll must be a no-op, not a double-unescape or corruption.
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
