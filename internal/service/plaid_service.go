package service

import (
	"context"
	"fmt"

	"github.com/google/uuid"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	"github.com/mauro-afa91/spendsense/internal/crypto"
	plaidclient "github.com/mauro-afa91/spendsense/internal/plaid"
	"github.com/mauro-afa91/spendsense/internal/repository"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

type PlaidService struct {
	plaid         plaidclient.Client
	items         repository.PlaidRepository
	budgets       repository.BudgetProfileRepository
	users         repository.UserRepository
	encryptionKey string
}

func NewPlaidService(
	plaid plaidclient.Client,
	items repository.PlaidRepository,
	budgets repository.BudgetProfileRepository,
	users repository.UserRepository,
	encryptionKey string,
) *PlaidService {
	return &PlaidService{plaid: plaid, items: items, budgets: budgets, users: users, encryptionKey: encryptionKey}
}

// requireUS returns Forbidden if the user is not a US resident.
func (s *PlaidService) requireUS(ctx context.Context, userID uuid.UUID) error {
	user, err := s.users.GetByID(ctx, userID)
	if err != nil {
		return err
	}
	cc := ""
	if user.CountryCode != nil {
		cc = *user.CountryCode
	}
	if cc != "US" {
		return apperr.Forbidden("Plaid is only available for US users")
	}
	return nil
}

// requireProfileOwnerOrMember returns Forbidden if the user does not own or belong to the profile.
func (s *PlaidService) requireProfileOwnerOrMember(ctx context.Context, profileID, userID uuid.UUID) error {
	profile, err := s.budgets.GetByID(ctx, profileID)
	if err != nil {
		return err
	}
	if profile.UserID == userID {
		return nil
	}
	ok, err := s.budgets.ExistsPersonForUser(ctx, profileID, userID)
	if err != nil {
		return err
	}
	if !ok {
		return apperr.Forbidden("access denied")
	}
	return nil
}

type CreateLinkTokenResult struct {
	LinkToken  string
	Expiration string
}

func (s *PlaidService) CreateLinkToken(ctx context.Context, userID, profileID uuid.UUID) (CreateLinkTokenResult, error) {
	if err := s.requireUS(ctx, userID); err != nil {
		return CreateLinkTokenResult{}, err
	}
	if err := s.requireProfileOwnerOrMember(ctx, profileID, userID); err != nil {
		return CreateLinkTokenResult{}, err
	}
	tok, exp, err := s.plaid.CreateLinkToken(ctx, userID.String())
	if err != nil {
		return CreateLinkTokenResult{}, fmt.Errorf("plaid: create link token: %w", err)
	}
	return CreateLinkTokenResult{LinkToken: tok, Expiration: exp}, nil
}

func (s *PlaidService) ExchangePublicToken(ctx context.Context, userID, profileID uuid.UUID, publicToken string) (db.PlaidItem, error) {
	if err := s.requireUS(ctx, userID); err != nil {
		return db.PlaidItem{}, err
	}
	if err := s.requireProfileOwnerOrMember(ctx, profileID, userID); err != nil {
		return db.PlaidItem{}, err
	}

	accessToken, itemID, err := s.plaid.ExchangePublicToken(ctx, publicToken)
	if err != nil {
		return db.PlaidItem{}, err
	}

	encryptedToken, err := crypto.Encrypt(accessToken, s.encryptionKey)
	if err != nil {
		return db.PlaidItem{}, fmt.Errorf("plaid: encrypt access token: %w", err)
	}

	// Fetch institution name; non-fatal if unavailable.
	var institutionName, institutionID *string
	// Plaid doesn't return institution_id directly from token exchange — it's on the Item.
	// For simplicity we leave both nil; a future enhancement can call GetItem then GetInstitution.

	item, err := s.items.Create(ctx, db.CreatePlaidItemParams{
		UserID:          userID,
		BudgetProfileID: profileID,
		AccessToken:     encryptedToken,
		ItemID:          itemID,
		InstitutionID:   institutionID,
		InstitutionName: institutionName,
	})
	if err != nil {
		return db.PlaidItem{}, fmt.Errorf("plaid: store item: %w", err)
	}
	return item, nil
}

func (s *PlaidService) GetConnections(ctx context.Context, userID uuid.UUID, profileID *uuid.UUID) ([]db.PlaidItem, error) {
	if err := s.requireUS(ctx, userID); err != nil {
		return nil, err
	}
	if profileID != nil {
		if err := s.requireProfileOwnerOrMember(ctx, *profileID, userID); err != nil {
			return nil, err
		}
		return s.items.ListByBudgetProfile(ctx, *profileID)
	}
	return s.items.ListByUser(ctx, userID)
}

func (s *PlaidService) Disconnect(ctx context.Context, userID, connectionID uuid.UUID) error {
	if err := s.requireUS(ctx, userID); err != nil {
		return err
	}
	item, err := s.items.GetByID(ctx, connectionID)
	if err != nil {
		return err
	}
	if item.UserID != userID {
		return apperr.Forbidden("access denied")
	}

	// Best-effort: notify Plaid that the item is being removed.
	if decrypted, err := crypto.Decrypt(item.AccessToken, s.encryptionKey); err == nil {
		_ = s.plaid.RemoveItem(ctx, decrypted)
	}

	_, err = s.items.UpdateStatus(ctx, db.UpdatePlaidItemStatusParams{
		ID:     connectionID,
		Status: "disconnected",
	})
	return err
}
