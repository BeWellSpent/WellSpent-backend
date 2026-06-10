package service

import (
	"context"
	"errors"
	"fmt"

	"github.com/google/uuid"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	"github.com/mauro-afa91/spendsense/internal/auth"
	"github.com/mauro-afa91/spendsense/internal/repository"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
	"golang.org/x/crypto/bcrypt"
)

type AuthService struct {
	users  repository.UserRepository
	jwt    *auth.JWTService
	google *auth.GoogleOAuth
}

func NewAuthService(users repository.UserRepository, jwt *auth.JWTService, google *auth.GoogleOAuth) *AuthService {
	return &AuthService{users: users, jwt: jwt, google: google}
}

type LoginResult struct {
	AccessToken string
	ExpiresIn   int64
}

func (s *AuthService) Login(ctx context.Context, email, password string) (LoginResult, error) {
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

	token, err := s.jwt.GenerateToken(user.ID)
	if err != nil {
		return LoginResult{}, fmt.Errorf("auth: generate token: %w", err)
	}
	return LoginResult{AccessToken: token, ExpiresIn: s.jwt.LifetimeSeconds()}, nil
}

type GoogleExchangeResult struct {
	AccessToken string
	ExpiresIn   int64
	IsNewUser   bool
}

func (s *AuthService) GoogleExchange(ctx context.Context, code, redirectURI string) (GoogleExchangeResult, error) {
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
			user, err = s.users.Create(ctx, db.CreateUserParams{
				Email:     info.Email,
				FirstName: &info.GivenName,
				LastName:  &info.FamilyName,
			})
			if err != nil {
				return GoogleExchangeResult{}, fmt.Errorf("auth: create user: %w", err)
			}
			isNew = true
		}
		userID = user.ID
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
	}

	token, err := s.jwt.GenerateToken(userID)
	if err != nil {
		return GoogleExchangeResult{}, fmt.Errorf("auth: generate token: %w", err)
	}
	return GoogleExchangeResult{AccessToken: token, ExpiresIn: s.jwt.LifetimeSeconds(), IsNewUser: isNew}, nil
}

func (s *AuthService) GoogleAuthURL(state string) string {
	return s.google.AuthCodeURL(state)
}
