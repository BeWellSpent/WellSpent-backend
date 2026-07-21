package service

import (
	"context"
	"errors"
	"fmt"
	"net/mail"
	"strings"
	"time"
	"unicode"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/auth"
	"github.com/BeWellSpent/wellspent-backend/internal/config"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	resend "github.com/resend/resend-go/v2"
	"go.uber.org/zap"
	"golang.org/x/crypto/bcrypt"
)

// defaultTokenLifetime is the JWT lifetime for Login (remember_me=false),
// Register, and Google OAuth exchange — every auth flow except Login's
// remember-me path. Deliberately independent of JWTService's configured
// lifetime (JWT_LIFETIME_SECONDS): that value is a leftover default meant
// for GenerateToken() callers, not a lifetime any flow should silently
// inherit. Register and ExchangeGoogleCode used to call GenerateToken()
// and inherit whatever JWT_LIFETIME_SECONDS happened to be, which could
// be far shorter than Login's 24h — the cookie/token lifetimes matched
// (both now derive from the RPC's real expires_in), but the actual
// session length for those two flows was inconsistent with Login and
// with what the auth spec documents.
const (
	defaultTokenLifetime    = 24 * time.Hour
	rememberMeTokenLifetime = 90 * 24 * time.Hour

	// emailVerificationTTL is the business-rule cap on how long a
	// verification link stays valid (issue #7: "maximum 10 minutes").
	emailVerificationTTL = 10 * time.Minute
	// verificationResendCooldown throttles ResendVerificationEmail so a
	// user (or an attacker) can't trigger unlimited emails to an address.
	verificationResendCooldown = 60 * time.Second
)

type AuthService struct {
	users  repository.UserRepository
	jwt    *auth.JWTService
	google *auth.GoogleOAuth
	cfg    *config.Config
	log    *zap.Logger
}

func NewAuthService(users repository.UserRepository, jwt *auth.JWTService, google *auth.GoogleOAuth, cfg *config.Config, log *zap.Logger) *AuthService {
	return &AuthService{users: users, jwt: jwt, google: google, cfg: cfg, log: log}
}

type LoginResult struct {
	AccessToken string
	ExpiresIn   int64
	Language    string
	Currency    string
}

func (s *AuthService) Login(ctx context.Context, email, password string, rememberMe bool) (LoginResult, error) {
	user, err := s.users.GetByEmail(ctx, email)
	if err != nil {
		// Surface as generic error to avoid email enumeration
		return LoginResult{}, apperr.Invalid("invalid email or password")
	}
	if !user.IsActive {
		return LoginResult{}, apperr.Forbidden("account is inactive")
	}
	if user.HashedPassword == nil {
		return LoginResult{}, apperr.Invalid("account uses OAuth login only")
	}
	if err := bcrypt.CompareHashAndPassword([]byte(*user.HashedPassword), []byte(password)); err != nil {
		return LoginResult{}, apperr.Invalid("invalid email or password")
	}

	lifetime := defaultTokenLifetime
	if rememberMe {
		lifetime = rememberMeTokenLifetime
	}
	token, err := s.jwt.GenerateTokenWithLifetime(user.ID, lifetime)
	if err != nil {
		return LoginResult{}, fmt.Errorf("auth: generate token: %w", err)
	}
	return LoginResult{AccessToken: token, ExpiresIn: int64(lifetime.Seconds()), Language: user.Language, Currency: user.Currency}, nil
}

type GoogleExchangeResult struct {
	AccessToken string
	ExpiresIn   int64
	IsNewUser   bool
	Language    string
	Currency    string
}

func (s *AuthService) GoogleExchange(ctx context.Context, code, redirectURI, language, currency string) (GoogleExchangeResult, error) {
	info, _, err := s.google.Exchange(ctx, code)
	if err != nil {
		return GoogleExchangeResult{}, fmt.Errorf("auth: google exchange: %w", err)
	}

	// Check if OAuth account already linked
	oauthAcc, err := s.users.GetOAuthAccount(ctx, db.GetOAuthAccountParams{
		OauthName: "google",
		AccountID: info.Sub,
	})

	var userID uuid.UUID
	var userLang, userCurrency string
	isNew := false

	if err != nil {
		var notFound *apperr.NotFoundError
		if !errors.As(err, &notFound) {
			return GoogleExchangeResult{}, err
		}
		// New OAuth user — try to find existing user by email or create
		user, err := s.users.GetByEmail(ctx, info.Email)
		if err != nil {
			var notFoundUser *apperr.NotFoundError
			if !errors.As(err, &notFoundUser) {
				return GoogleExchangeResult{}, err
			}
			// Create brand new user
			lang := language
			if lang == "" {
				lang = "en"
			}
			cur := currency
			if cur == "" {
				cur = "USD"
			}
			user, err = s.users.Create(ctx, db.CreateUserParams{
				Email:     info.Email,
				FirstName: &info.GivenName,
				LastName:  &info.FamilyName,
				Language:  lang,
				Currency:  cur,
			})
			if err != nil {
				return GoogleExchangeResult{}, fmt.Errorf("auth: create user: %w", err)
			}
			// Google already proved ownership of this email address —
			// skip the token/email verification flow entirely.
			if err := s.users.MarkVerified(ctx, user.ID); err != nil {
				return GoogleExchangeResult{}, fmt.Errorf("auth: mark verified: %w", err)
			}
			isNew = true
		}
		if !isNew {
			// Existing email/password user linking Google for the first time.
			// Google proved ownership of this address — auto-verify the account.
			if verifyErr := s.users.MarkVerified(ctx, user.ID); verifyErr != nil {
				return GoogleExchangeResult{}, fmt.Errorf("auth: mark verified: %w", verifyErr)
			}
		}
		userID = user.ID
		userLang = user.Language
		userCurrency = user.Currency
		// Link OAuth account
		_, err = s.users.CreateOAuthAccount(ctx, db.CreateOAuthAccountParams{
			UserID:       userID,
			OauthName:    "google",
			AccountID:    info.Sub,
			AccountEmail: info.Email,
		})
		if err != nil {
			return GoogleExchangeResult{}, fmt.Errorf("auth: link oauth account: %w", err)
		}
	} else {
		userID = oauthAcc.UserID
		existing, fetchErr := s.users.GetByID(ctx, userID)
		if fetchErr != nil {
			return GoogleExchangeResult{}, fmt.Errorf("auth: get user: %w", fetchErr)
		}
		userLang = existing.Language
		userCurrency = existing.Currency
	}

	// Google OAuth has no "remember me" UI — always issue a persistent token
	// so the session lifetime matches what a user would get by checking
	// "remember me" on the email/password login form.
	token, err := s.jwt.GenerateTokenWithLifetime(userID, rememberMeTokenLifetime)
	if err != nil {
		return GoogleExchangeResult{}, fmt.Errorf("auth: generate token: %w", err)
	}
	return GoogleExchangeResult{AccessToken: token, ExpiresIn: int64(rememberMeTokenLifetime.Seconds()), IsNewUser: isNew, Language: userLang, Currency: userCurrency}, nil
}

func (s *AuthService) GoogleAuthURL(state string) string {
	return s.google.AuthCodeURL(state)
}

type RegisterResult struct {
	AccessToken string
	ExpiresIn   int64
}

func (s *AuthService) Register(ctx context.Context, email, password, firstName, lastName, countryCode, stateCode, language, currency string) (RegisterResult, error) {
	email = strings.ToLower(strings.TrimSpace(email))
	if _, err := mail.ParseAddress(email); err != nil {
		return RegisterResult{}, apperr.Invalid("invalid email address")
	}
	if err := validatePassword(password); err != nil {
		return RegisterResult{}, err
	}

	_, err := s.users.GetByEmail(ctx, email)
	if err == nil {
		return RegisterResult{}, apperr.Duplicate("user", "email", email)
	}
	var notFound *apperr.NotFoundError
	if !errors.As(err, &notFound) {
		return RegisterResult{}, err
	}

	hash, err := bcrypt.GenerateFromPassword([]byte(password), 12)
	if err != nil {
		return RegisterResult{}, fmt.Errorf("auth: hash password: %w", err)
	}
	hashed := string(hash)
	fn := firstName
	ln := lastName
	lang := language
	if lang == "" {
		lang = "en"
	}
	cur := currency
	if cur == "" {
		cur = "USD"
	}
	params := db.CreateUserParams{
		Email:          email,
		HashedPassword: &hashed,
		FirstName:      &fn,
		LastName:       &ln,
		Language:       lang,
		Currency:       cur,
	}
	if countryCode != "" {
		params.CountryCode = &countryCode
	}
	if stateCode != "" {
		params.StateCode = &stateCode
	}
	user, err := s.users.Create(ctx, params)
	if err != nil {
		return RegisterResult{}, fmt.Errorf("auth: create user: %w", err)
	}

	// Best-effort — the account is already created; a failed send just
	// means the user falls back to "resend verification email" later.
	if err := s.sendVerificationEmail(ctx, user); err != nil {
		s.log.Error("auth.verification_email.failed", zap.String("to", user.Email), zap.Error(err))
	}

	token, err := s.jwt.GenerateTokenWithLifetime(user.ID, defaultTokenLifetime)
	if err != nil {
		return RegisterResult{}, fmt.Errorf("auth: generate token: %w", err)
	}
	return RegisterResult{AccessToken: token, ExpiresIn: int64(defaultTokenLifetime.Seconds())}, nil
}

// VerifyEmail redeems a verification token minted by sendVerificationEmail.
// Errors are deliberately generic (mirrors Login's invalid-credentials
// message) so a caller can't distinguish "wrong token" from "expired token"
// from "never existed".
func (s *AuthService) VerifyEmail(ctx context.Context, token string) error {
	tok, err := uuid.Parse(token)
	if err != nil {
		return apperr.Invalid("invalid or expired verification token")
	}
	user, err := s.users.GetByVerificationToken(ctx, tok)
	if err != nil {
		var notFound *apperr.NotFoundError
		if errors.As(err, &notFound) {
			return apperr.Invalid("invalid or expired verification token")
		}
		return err
	}
	if !user.EmailVerificationExpiresAt.Valid || user.EmailVerificationExpiresAt.Time.Before(time.Now().UTC()) {
		return apperr.Invalid("invalid or expired verification token")
	}
	if err := s.users.MarkVerified(ctx, user.ID); err != nil {
		return fmt.Errorf("auth: mark verified: %w", err)
	}
	return nil
}

// ResendVerificationEmail re-mints and re-sends a verification token.
// Always succeeds for unknown or already-verified emails (no error) to
// avoid leaking account existence/status — only a too-soon repeat request
// for a real, unverified account is rejected, matching the cooldown.
func (s *AuthService) ResendVerificationEmail(ctx context.Context, email string) error {
	email = strings.ToLower(strings.TrimSpace(email))
	user, err := s.users.GetByEmail(ctx, email)
	if err != nil {
		var notFound *apperr.NotFoundError
		if errors.As(err, &notFound) {
			return nil
		}
		return err
	}
	if user.IsVerified {
		return nil
	}
	if user.EmailVerificationLastSentAt.Valid &&
		time.Since(user.EmailVerificationLastSentAt.Time) < verificationResendCooldown {
		return apperr.Invalid("please wait before requesting another verification email")
	}
	return s.sendVerificationEmail(ctx, user)
}

// sendVerificationEmail mints a fresh token (10-minute TTL) and emails it.
// Non-fatal Resend failures are the caller's responsibility to log —
// this returns the error rather than swallowing it so Register and
// ResendVerificationEmail can each decide how to surface it.
func (s *AuthService) sendVerificationEmail(ctx context.Context, user db.User) error {
	token := uuid.New()
	now := time.Now().UTC()
	if _, err := s.users.SetEmailVerificationToken(ctx, db.SetEmailVerificationTokenParams{
		ID:         user.ID,
		Token:      &token,
		ExpiresAt:  pgtype.Timestamptz{Time: now.Add(emailVerificationTTL), Valid: true},
		LastSentAt: pgtype.Timestamptz{Time: now, Valid: true},
	}); err != nil {
		return fmt.Errorf("auth: set verification token: %w", err)
	}

	if s.cfg.ResendAPIKey == "" {
		s.log.Warn("auth.verification_email.skipped: RESEND_API_KEY not set", zap.String("to", user.Email))
		return nil
	}
	client := resend.NewClient(s.cfg.ResendAPIKey)
	link := fmt.Sprintf("%s/en/verify-email/%s", strings.TrimRight(s.cfg.FrontendURL, "/"), token.String())
	body := fmt.Sprintf(
		`<p>Welcome to WellSpent! Please confirm your email address to finish setting up your account.</p>`+
			`<p><a href="%s" style="display:inline-block;padding:10px 20px;background:#1976d2;color:#fff;text-decoration:none;border-radius:4px;">Verify email</a></p>`+
			`<p>If the button above doesn't work, copy and paste this link into your browser:</p>`+
			`<p>%s</p>`+
			`<p>This link expires in 10 minutes.</p>`,
		link, link,
	)
	_, err := client.Emails.Send(&resend.SendEmailRequest{
		From:    s.cfg.ResendFromEmail,
		To:      []string{user.Email},
		Subject: "Verify your WellSpent email address",
		Html:    body,
	})
	if err != nil {
		return err
	}
	s.log.Info("auth.verification_email.sent", zap.String("to", user.Email))
	return nil
}

func validatePassword(password string) error {
	if len(password) < 8 {
		return apperr.Invalid("password must be at least 8 characters")
	}
	var hasUpper, hasLower, hasDigit, hasSpecial bool
	for _, c := range password {
		switch {
		case unicode.IsUpper(c):
			hasUpper = true
		case unicode.IsLower(c):
			hasLower = true
		case unicode.IsDigit(c):
			hasDigit = true
		case unicode.IsPunct(c) || unicode.IsSymbol(c):
			hasSpecial = true
		}
	}
	if !hasUpper || !hasLower || !hasDigit || !hasSpecial {
		return apperr.Invalid("password must contain uppercase, lowercase, digit, and special character")
	}
	return nil
}
