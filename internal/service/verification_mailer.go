package service

import (
	"context"
	"fmt"
	"strings"
	"time"

	"github.com/BeWellSpent/wellspent-backend/internal/config"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	resend "github.com/resend/resend-go/v2"
	"go.uber.org/zap"
)

// emailVerificationTTL is the business-rule cap on how long a verification
// link stays valid (issue #7: "maximum 10 minutes").
const emailVerificationTTL = 10 * time.Minute

// VerificationMailer mints an email-verification token and sends the link.
//
// Lives on its own rather than as a method on AuthService because two
// services need it: AuthService (registration, resend) and UserService
// (changing the address, which re-opens verification for the new one).
type VerificationMailer struct {
	users repository.UserRepository
	cfg   *config.Config
	log   *zap.Logger
}

func NewVerificationMailer(users repository.UserRepository, cfg *config.Config, log *zap.Logger) *VerificationMailer {
	if users == nil {
		panic("NewVerificationMailer: users is required")
	}
	if cfg == nil {
		panic("NewVerificationMailer: cfg is required")
	}
	if log == nil {
		panic("NewVerificationMailer: log is required")
	}
	return &VerificationMailer{users: users, cfg: cfg, log: log}
}

// Send mints a fresh token (10-minute TTL) and emails it.
//
// Returns the error rather than swallowing it, so each caller decides how to
// surface a failed send — every current caller treats it as non-fatal, since
// the user can always fall back to "resend verification email".
func (m *VerificationMailer) Send(ctx context.Context, user db.User) error {
	token := uuid.New()
	now := time.Now().UTC()
	if _, err := m.users.SetEmailVerificationToken(ctx, db.SetEmailVerificationTokenParams{
		ID:         user.ID,
		Token:      &token,
		ExpiresAt:  pgtype.Timestamptz{Time: now.Add(emailVerificationTTL), Valid: true},
		LastSentAt: pgtype.Timestamptz{Time: now, Valid: true},
	}); err != nil {
		return fmt.Errorf("auth: set verification token: %w", err)
	}

	if m.cfg.ResendAPIKey == "" {
		m.log.Warn("auth.verification_email.skipped: RESEND_API_KEY not set", zap.String("to", user.Email))
		return nil
	}
	client := resend.NewClient(m.cfg.ResendAPIKey)
	link := fmt.Sprintf("%s/en/verify-email/%s", strings.TrimRight(m.cfg.FrontendURL, "/"), token.String())
	body := fmt.Sprintf(
		`<p>Welcome to WellSpent! Please confirm your email address to finish setting up your account.</p>`+
			`<p><a href="%s" style="display:inline-block;padding:10px 20px;background:#1976d2;color:#fff;text-decoration:none;border-radius:4px;">Verify email</a></p>`+
			`<p>If the button above doesn't work, copy and paste this link into your browser:</p>`+
			`<p>%s</p>`+
			`<p>This link expires in 10 minutes.</p>`,
		link, link,
	)
	_, err := client.Emails.Send(&resend.SendEmailRequest{
		From:    m.cfg.ResendFromEmail,
		To:      []string{user.Email},
		Subject: "Verify your WellSpent email address",
		Html:    body,
	})
	if err != nil {
		return err
	}
	m.log.Info("auth.verification_email.sent", zap.String("to", user.Email))
	return nil
}
