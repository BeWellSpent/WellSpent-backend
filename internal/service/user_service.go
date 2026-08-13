package service

import (
	"context"
	"errors"
	"fmt"
	"net/mail"
	"strings"

	"github.com/google/uuid"
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/auth"
	"github.com/BeWellSpent/wellspent-backend/internal/config"
	"github.com/BeWellSpent/wellspent-backend/internal/crypto"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"go.uber.org/zap"
	"golang.org/x/crypto/bcrypt"
)

type UserService struct {
	users repository.UserRepository
	// apple is only used to revoke credentials on account deletion. Nil is
	// tolerated (unlike the other services' required deps) so the many
	// existing call sites that only exercise profile reads/writes don't all
	// have to grow an Apple client they never touch.
	apple         auth.AppleAuthenticator
	encryptionKey string
	log           *zap.Logger
	mailer        *VerificationMailer
}

func NewUserService(users repository.UserRepository, apple auth.AppleAuthenticator, encryptionKey string, cfg *config.Config, log *zap.Logger) *UserService {
	if users == nil {
		panic("NewUserService: users is required")
	}
	if cfg == nil {
		panic("NewUserService: cfg is required")
	}
	if log == nil {
		panic("NewUserService: log is required")
	}
	return &UserService{
		users:         users,
		apple:         apple,
		encryptionKey: encryptionKey,
		log:           log,
		mailer:        NewVerificationMailer(users, cfg, log),
	}
}

func (s *UserService) GetByID(ctx context.Context, id uuid.UUID) (db.User, error) {
	return s.users.GetByID(ctx, id)
}

type UpdateUserInput struct {
	FirstName           *string
	LastName            *string
	CountryCode         *string
	StateCode           *string
	FilingStatus        string
	TaxPaymentFrequency int32
	Language            string
	Currency            string
}

func (s *UserService) Update(ctx context.Context, id uuid.UUID, inp UpdateUserInput) (db.User, error) {
	return s.users.Update(ctx, db.UpdateUserParams{
		ID:                  id,
		FirstName:           inp.FirstName,
		LastName:            inp.LastName,
		CountryCode:         inp.CountryCode,
		StateCode:           inp.StateCode,
		FilingStatus:        inp.FilingStatus,
		TaxPaymentFrequency: inp.TaxPaymentFrequency,
		Language:            inp.Language,
		Currency:            inp.Currency,
	})
}

// ListCountries returns all enabled countries with their feature flags merged in.
func (s *UserService) ListCountries(ctx context.Context) ([]db.ListEnabledCountriesRow, map[string][]db.CountryFeature, error) {
	countries, err := s.users.ListEnabledCountries(ctx)
	if err != nil {
		return nil, nil, err
	}
	features, err := s.users.ListCountryFeatures(ctx)
	if err != nil {
		return nil, nil, err
	}
	byCountry := make(map[string][]db.CountryFeature)
	for _, f := range features {
		byCountry[f.CountryCode] = append(byCountry[f.CountryCode], f)
	}
	return countries, byCountry, nil
}

func (s *UserService) ChangePassword(ctx context.Context, id uuid.UUID, currentPassword, newPassword string) error {
	user, err := s.users.GetByID(ctx, id)
	if err != nil {
		return err
	}
	if user.HashedPassword == nil {
		return apperr.Invalid("account uses OAuth login only")
	}
	if err := bcrypt.CompareHashAndPassword([]byte(*user.HashedPassword), []byte(currentPassword)); err != nil {
		return apperr.Invalid("current password is incorrect")
	}
	hashed, err := bcrypt.GenerateFromPassword([]byte(newPassword), 12)
	if err != nil {
		return fmt.Errorf("user: hash password: %w", err)
	}
	hashedStr := string(hashed)
	return s.users.UpdatePassword(ctx, db.UpdateUserPasswordParams{
		ID:             id,
		HashedPassword: &hashedStr,
	})
}

// ChangeEmail replaces the account's address and re-opens verification for
// the new one, then sends the verification link.
//
// This is the only recourse for an address mistyped at registration — resend
// can't help, since it sends to the same wrong address — which matters now
// that both clients block an unverified account outright.
//
// Deliberately does not ask for the current password, unlike ChangePassword:
// the JWT already proves the session, and the new address grants nothing
// until its own verification link is redeemed. Requiring a password would
// also lock out an OAuth account, which has none.
func (s *UserService) ChangeEmail(ctx context.Context, id uuid.UUID, newEmail string) (db.User, error) {
	email := strings.ToLower(strings.TrimSpace(newEmail))
	if _, err := mail.ParseAddress(email); err != nil {
		return db.User{}, apperr.Invalid("invalid email address")
	}

	current, err := s.users.GetByID(ctx, id)
	if err != nil {
		return db.User{}, err
	}
	if current.Email == email {
		// Silently succeeding here would be worse than it looks: the caller
		// is a user staring at a verification wall, and "saved" with nothing
		// arriving reads as the feature being broken.
		return db.User{}, apperr.Invalid("that is already your email address")
	}

	existing, err := s.users.GetByEmail(ctx, email)
	if err == nil && existing.ID != id {
		return db.User{}, apperr.Duplicate("user", "email", email)
	}
	var notFound *apperr.NotFoundError
	if err != nil && !errors.As(err, &notFound) {
		return db.User{}, err
	}

	updated, err := s.users.UpdateEmail(ctx, db.UpdateUserEmailParams{ID: id, Email: email})
	if err != nil {
		return db.User{}, err
	}

	// Best-effort, matching Register: the address is already changed, and the
	// user can fall back to "resend verification email" — which now goes to
	// the corrected address.
	if err := s.mailer.Send(ctx, updated); err != nil {
		s.log.Error("user.change_email.verification_email_failed", zap.String("to", updated.Email), zap.Error(err))
	}
	return updated, nil
}

func (s *UserService) Delete(ctx context.Context, id uuid.UUID) error {
	s.revokeAppleTokens(ctx, id)
	return s.users.SoftDelete(ctx, id)
}

// revokeAppleTokens tells Apple to invalidate the user's credentials for this
// app, which App Store Review 5.1.1(v) requires of any app offering Sign in
// with Apple.
//
// Every failure is logged and swallowed: a user who asked to delete their
// account must end up deleted regardless of whether Apple's endpoint was
// reachable, and the deletion is a soft-delete the user can only undo through
// support anyway.
func (s *UserService) revokeAppleTokens(ctx context.Context, userID uuid.UUID) {
	if s.apple == nil {
		return
	}
	accounts, err := s.users.ListOAuthAccountsByUser(ctx, userID)
	if err != nil {
		s.log.Error("user.delete.list_oauth_accounts_failed", zap.String("user_id", userID.String()), zap.Error(err))
		return
	}
	for _, acc := range accounts {
		if acc.OauthName != oauthProviderApple || acc.RefreshToken == nil || *acc.RefreshToken == "" {
			continue
		}
		if s.encryptionKey == "" {
			s.log.Error("user.delete.apple_revoke_skipped: ENCRYPTION_KEY unset", zap.String("user_id", userID.String()))
			return
		}
		refreshToken, err := crypto.Decrypt(*acc.RefreshToken, s.encryptionKey)
		if err != nil {
			s.log.Error("user.delete.apple_refresh_token_decrypt_failed", zap.String("user_id", userID.String()), zap.Error(err))
			continue
		}
		if err := s.apple.RevokeRefreshToken(ctx, refreshToken); err != nil {
			s.log.Error("user.delete.apple_revoke_failed", zap.String("user_id", userID.String()), zap.Error(err))
			continue
		}
		s.log.Info("user.delete.apple_revoked", zap.String("user_id", userID.String()))
	}
}

// userDisplayName renders a user's name for other people to read, falling
// back to their email when they haven't set one.
//
// Mirrors the COALESCE/NULLIF/TRIM/CONCAT expression used wherever the same
// name is derived in SQL (see ListActivePlaidItemsWithOwnerByBudgetProfile),
// so a member's name doesn't differ depending on which layer produced it.
func userDisplayName(u db.User) string {
	parts := []string{}
	if u.FirstName != nil {
		parts = append(parts, *u.FirstName)
	}
	if u.LastName != nil {
		parts = append(parts, *u.LastName)
	}
	if name := strings.TrimSpace(strings.Join(parts, " ")); name != "" {
		return name
	}
	return u.Email
}
