package service

import (
	"context"
	"fmt"

	"github.com/google/uuid"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	"github.com/mauro-afa91/spendsense/internal/repository"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
	"golang.org/x/crypto/bcrypt"
)

type UserService struct {
	users repository.UserRepository
}

func NewUserService(users repository.UserRepository) *UserService {
	return &UserService{users: users}
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

func (s *UserService) Delete(ctx context.Context, id uuid.UUID) error {
	return s.users.Delete(ctx, id)
}
