package main

import (
	"context"
	"fmt"
	"html"
	"log"
	"os"
	"strconv"
	"strings"
	"time"

	"github.com/BeWellSpent/wellspent-backend/internal/config"
	"github.com/BeWellSpent/wellspent-backend/internal/db"
	plaidclient "github.com/BeWellSpent/wellspent-backend/internal/plaid"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	sqlcdb "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	resend "github.com/resend/resend-go/v2"
	"go.uber.org/zap"
)

// plaid-sync fetches incremental transaction changes from Plaid for every active
// plaid_item, then writes new/updated/removed Variable transactions into the
// matching budget period with Plaid-resolved categories.
func main() {
	dbURL := os.Getenv("DATABASE_URL")
	if dbURL == "" {
		log.Fatal("DATABASE_URL is required")
	}
	clientID := os.Getenv("PLAID_CLIENT_ID")
	secret := os.Getenv("PLAID_SECRET")
	plaidEnv := os.Getenv("PLAID_ENV")
	encryptionKey := os.Getenv("ENCRYPTION_KEY")
	if clientID == "" || secret == "" {
		log.Fatal("PLAID_CLIENT_ID and PLAID_SECRET are required")
	}
	if encryptionKey == "" {
		log.Fatal("ENCRYPTION_KEY is required")
	}
	if plaidEnv == "" {
		plaidEnv = "sandbox"
	}
	maxRetries := envIntDefault("PLAID_HTTP_MAX_RETRIES", 3)
	retryDelay := envDurationDefault("PLAID_HTTP_RETRY_DELAY", 5*time.Second)
	redactSensitive := envBoolDefault("PLAID_LOG_REDACT_SENSITIVE", true)

	// Read once here and reused both by the per-user alert_subscription
	// notifications (via notifCfg below) and the ops failure-alert email
	// further down, rather than reading RESEND_API_KEY/RESEND_FROM_EMAIL twice.
	resendAPIKey := os.Getenv("RESEND_API_KEY")
	resendFromEmail := os.Getenv("RESEND_FROM_EMAIL")
	if resendFromEmail == "" {
		resendFromEmail = "WellSpent <noreply@wellspent.app>"
	}
	notifCfg := &config.Config{
		ResendAPIKey:    resendAPIKey,
		ResendFromEmail: resendFromEmail,
		FrontendURL:     envStringDefault("FRONTEND_URL", "http://localhost:3000"),
		APNSKeyID:       os.Getenv("APNS_KEY_ID"),
		APNSTeamID:      os.Getenv("APNS_TEAM_ID"),
		APNSAuthKey:     config.NormalizePEMKey(os.Getenv("APNS_AUTH_KEY")),
		APNSBundleID:    envStringDefault("APNS_BUNDLE_ID", "com.bewellspent.WellSpent"),
		APNSEnvironment: envStringDefault("APNS_ENVIRONMENT", "sandbox"),
	}

	var logger *zap.Logger
	if os.Getenv("DEBUG") == "true" {
		logger, _ = zap.NewDevelopment()
	} else {
		logger, _ = zap.NewProduction()
	}
	defer logger.Sync() //nolint:errcheck

	// ENV isn't set by the Cloud Run Job deploy step (only PLAID_ENV is), so
	// an unset ENV here means "the real prod job" — a local test run against
	// dev is the only case that would set it explicitly.
	env := envStringDefault("ENV", "prod")

	ctx := context.Background()
	pool, err := db.NewPool(ctx, dbURL, fmt.Sprintf("wellspent-plaid-sync-%s", env))
	if err != nil {
		log.Fatalf("db: %v", err)
	}
	defer pool.Close()

	queries := sqlcdb.New(pool)
	plaidRepo := repository.NewPlaidRepository(queries)
	budgetRepo := repository.NewBudgetProfileRepository(queries)
	userRepo := repository.NewUserRepository(queries)
	txRepo := repository.NewTransactionRepository(queries)
	feRepo := repository.NewFixedExpenseRepository(queries)
	reviewRepo := repository.NewTransactionReviewRepository(queries)
	notifRepo := repository.NewNotificationRepository(queries)
	allocationRepo := repository.NewExpenseAllocationRepository(queries)

	pc, err := plaidclient.New(clientID, secret, plaidEnv, plaidclient.Options{
		Logger:          logger,
		RedactSensitive: redactSensitive,
		MaxRetries:      maxRetries,
		RetryDelay:      retryDelay,
	})
	if err != nil {
		log.Fatalf("plaid: init client: %v", err)
	}

	notifSvc := service.NewNotificationService(notifRepo, txRepo, budgetRepo, allocationRepo, userRepo, notifCfg, logger)
	svc := service.NewPlaidService(pc, plaidRepo, budgetRepo, userRepo, txRepo, feRepo, reviewRepo, encryptionKey).WithNotifications(notifSvc)

	profiles, err := svc.SyncAll(ctx)
	if err != nil {
		log.Fatalf("list active items: %v", err)
	}

	failures, skipped := reportRun(profiles)

	if len(failures) == 0 && len(skipped) == 0 {
		return
	}

	alertEmail := os.Getenv("PLAID_SYNC_ALERT_EMAIL")

	if resendAPIKey == "" || alertEmail == "" {
		log.Printf("plaid-sync: %d item(s) failed and %d skipped, but no notification sent — set RESEND_API_KEY and PLAID_SYNC_ALERT_EMAIL to enable it", len(failures), len(skipped))
		return
	}
	if err := sendFailureEmail(resendAPIKey, resendFromEmail, alertEmail, failures, skipped); err != nil {
		log.Printf("plaid-sync: failed to send notification email: %v", err)
	} else {
		log.Printf("plaid-sync: sent notification email to %s for %d failure(s) and %d skip(s)", alertEmail, len(failures), len(skipped))
	}
}

type syncFailure struct {
	Profile     string
	Institution string
	ItemID      string
	Err         error
}

// syncSkip is a connection the job deliberately did not sync because its
// owner isn't entitled to Plaid. Reported separately from failures: nothing
// is broken, but leaving it invisible is how one connection sat unsynced for
// over two weeks without anyone noticing.
type syncSkip struct {
	Profile     string
	Institution string
	ItemID      string
}

// reportRun logs the outcome per budget profile and collects everything worth
// alerting on. Logging by profile rather than by bare item UUID is the point:
// a failure line that names the budget and the institution can be acted on
// from the log alone.
func reportRun(profiles []service.ProfileSyncResult) ([]syncFailure, []syncSkip) {
	var failures []syncFailure
	var skipped []syncSkip

	log.Printf("plaid-sync: %d budget profile(s) due", len(profiles))
	for _, profile := range profiles {
		pid := profile.ProfileID.String()
		for _, item := range profile.Items {
			inst := item.InstitutionName
			if inst == "" {
				inst = "(unknown institution)"
			}
			switch {
			case item.Err != nil:
				log.Printf("plaid-sync: profile %s: %s (item %s) FAILED: %v", pid, inst, item.ItemID, item.Err)
				failures = append(failures, syncFailure{Profile: pid, Institution: inst, ItemID: item.ItemID.String(), Err: item.Err})
			case item.SkippedUnentitled:
				log.Printf("plaid-sync: profile %s: %s (item %s) SKIPPED — owner is not on a paid plan", pid, inst, item.ItemID)
				skipped = append(skipped, syncSkip{Profile: pid, Institution: inst, ItemID: item.ItemID.String()})
			default:
				log.Printf("plaid-sync: profile %s: %s (item %s) ok — %s", pid, inst, item.ItemID, describeAccounts(item))
			}
		}
	}
	return failures, skipped
}

// describeAccounts renders the per-account import counts for one connection,
// so the log says which account the transactions came from rather than only
// how many arrived.
func describeAccounts(item service.ItemSyncResult) string {
	if len(item.ByAccount) == 0 {
		return fmt.Sprintf("no new transactions (%d modified, %d removed)", item.Modified, item.Removed)
	}
	parts := make([]string, 0, len(item.ByAccount))
	for _, acct := range item.ByAccount {
		parts = append(parts, fmt.Sprintf("%s: %d", acct.Account, acct.Count))
	}
	return fmt.Sprintf("imported %s (%d auto-confirmed, %d queued for review)",
		strings.Join(parts, ", "), item.AutoConfirmed, item.Queued)
}

// buildFailureEmail renders a plain summary of everything worth attention in
// this run, so an ops recipient can see what happened without digging through
// Cloud Run logs.
//
// Skips are listed separately from failures because they aren't errors —
// nothing retried will fix them, they need someone to upgrade a plan — but
// they were previously invisible, which let a connection sit unsynced for
// over two weeks.
func buildFailureEmail(failures []syncFailure, skipped []syncSkip) (subject, body string) {
	switch {
	case len(failures) > 0 && len(skipped) > 0:
		subject = fmt.Sprintf("WellSpent Plaid sync: %d failed, %d skipped", len(failures), len(skipped))
	case len(failures) > 0:
		subject = fmt.Sprintf("WellSpent Plaid sync: %d item(s) failed", len(failures))
	default:
		subject = fmt.Sprintf("WellSpent Plaid sync: %d connection(s) skipped", len(skipped))
	}

	var sb strings.Builder
	if len(failures) > 0 {
		fmt.Fprintf(&sb, "<p>%d Plaid connection(s) failed during this run:</p><ul>", len(failures))
		for _, f := range failures {
			fmt.Fprintf(&sb, "<li>budget <code>%s</code> — %s (<code>%s</code>): %s</li>",
				html.EscapeString(f.Profile), html.EscapeString(f.Institution),
				html.EscapeString(f.ItemID), html.EscapeString(f.Err.Error()))
		}
		sb.WriteString("</ul>")
	}
	if len(skipped) > 0 {
		fmt.Fprintf(&sb, "<p>%d connection(s) skipped — the member who linked them is not on a paid plan, so they will never sync:</p><ul>", len(skipped))
		for _, s := range skipped {
			fmt.Fprintf(&sb, "<li>budget <code>%s</code> — %s (<code>%s</code>)</li>",
				html.EscapeString(s.Profile), html.EscapeString(s.Institution), html.EscapeString(s.ItemID))
		}
		sb.WriteString("</ul>")
	}
	return subject, sb.String()
}

func sendFailureEmail(apiKey, fromEmail, toEmail string, failures []syncFailure, skipped []syncSkip) error {
	subject, body := buildFailureEmail(failures, skipped)
	client := resend.NewClient(apiKey)
	_, err := client.Emails.Send(&resend.SendEmailRequest{
		From:    fromEmail,
		To:      []string{toEmail},
		Subject: subject,
		Html:    body,
	})
	return err
}

func envIntDefault(key string, def int) int {
	v := os.Getenv(key)
	if v == "" {
		return def
	}
	i, err := strconv.Atoi(v)
	if err != nil {
		return def
	}
	return i
}

func envDurationDefault(key string, def time.Duration) time.Duration {
	v := os.Getenv(key)
	if v == "" {
		return def
	}
	d, err := time.ParseDuration(v)
	if err != nil {
		return def
	}
	return d
}

func envBoolDefault(key string, def bool) bool {
	v := os.Getenv(key)
	if v == "" {
		return def
	}
	b, err := strconv.ParseBool(v)
	if err != nil {
		return def
	}
	return b
}

func envStringDefault(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}
