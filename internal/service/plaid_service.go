package service

import (
	"context"
	"fmt"
	"log"

	"github.com/google/uuid"
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/crypto"
	plaidclient "github.com/BeWellSpent/wellspent-backend/internal/plaid"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
)

type PlaidService struct {
	plaid         plaidclient.Client
	items         repository.PlaidRepository
	budgets       repository.BudgetProfileRepository
	users         repository.UserRepository
	transactions  repository.TransactionRepository
	fixedExpenses repository.FixedExpenseRepository
	reviews       repository.TransactionReviewRepository
	encryptionKey string
}

func NewPlaidService(
	plaid plaidclient.Client,
	items repository.PlaidRepository,
	budgets repository.BudgetProfileRepository,
	users repository.UserRepository,
	transactions repository.TransactionRepository,
	fixedExpenses repository.FixedExpenseRepository,
	reviews repository.TransactionReviewRepository,
	encryptionKey string,
) *PlaidService {
	return &PlaidService{
		plaid:         plaid,
		items:         items,
		budgets:       budgets,
		users:         users,
		transactions:  transactions,
		fixedExpenses: fixedExpenses,
		reviews:       reviews,
		encryptionKey: encryptionKey,
	}
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

// CreateLinkToken creates a Link token. If connectionID is non-nil, it
// requests update mode (account selection) for that existing connection
// instead of a fresh connect flow — the caller must own the connection.
func (s *PlaidService) CreateLinkToken(ctx context.Context, userID, profileID uuid.UUID, connectionID *uuid.UUID) (CreateLinkTokenResult, error) {
	if err := s.requireUS(ctx, userID); err != nil {
		return CreateLinkTokenResult{}, err
	}
	if err := s.requireProfileOwnerOrMember(ctx, profileID, userID); err != nil {
		return CreateLinkTokenResult{}, err
	}

	updateAccessToken := ""
	if connectionID != nil {
		item, err := s.items.GetByID(ctx, *connectionID)
		if err != nil {
			return CreateLinkTokenResult{}, err
		}
		if item.UserID != userID {
			return CreateLinkTokenResult{}, apperr.Forbidden("access denied")
		}
		decrypted, err := crypto.Decrypt(item.AccessToken, s.encryptionKey)
		if err != nil {
			return CreateLinkTokenResult{}, fmt.Errorf("plaid: decrypt access token: %w", err)
		}
		updateAccessToken = decrypted
	}

	tok, exp, err := s.plaid.CreateLinkToken(ctx, userID.String(), updateAccessToken)
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

	// Fetch linked accounts and institution info.
	accounts, institutionID, err := s.plaid.GetAccounts(ctx, accessToken)
	if err != nil {
		// Non-fatal: store the item anyway; payment methods can be created later.
		accounts = nil
		institutionID = ""
	}

	// Resolve institution display name.
	var instIDPtr, instNamePtr *string
	if institutionID != "" {
		instIDPtr = &institutionID
		if name, nameErr := s.plaid.GetInstitutionName(ctx, institutionID); nameErr == nil && name != "" {
			instNamePtr = &name
		}
	}

	item, err := s.items.Create(ctx, db.CreatePlaidItemParams{
		UserID:          userID,
		BudgetProfileID: profileID,
		AccessToken:     encryptedToken,
		ItemID:          itemID,
		InstitutionID:   instIDPtr,
		InstitutionName: instNamePtr,
	})
	if err != nil {
		return db.PlaidItem{}, fmt.Errorf("plaid: store item: %w", err)
	}

	// Create one payment method per linked account, attributed to this user's
	// budget person row. Non-fatal: item is already stored above.
	pmCreated := s.createMissingPaymentMethods(ctx, item, userID, accounts)
	instName := ""
	if instNamePtr != nil {
		instName = *instNamePtr
	}
	log.Printf("plaid: item %s connected — institution=%q user=%s %d payment method(s) created", item.ItemID, instName, userID, pmCreated)

	// Trigger an immediate sync so transactions appear right after connecting.
	go func() {
		if err := s.SyncItem(context.Background(), item); err != nil {
			log.Printf("plaid: initial sync for item %s: %v", item.ID, err)
		}
	}()

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

// createMissingPaymentMethods creates a payment method for any account not
// already represented (by plaid_account_id, or by a name-match fallback
// for accounts Plaid re-IDs on reconnect). Non-fatal per-account — the
// caller's item/connection is already persisted regardless of outcome here.
// Returns the number of methods actually created.
func (s *PlaidService) createMissingPaymentMethods(ctx context.Context, item db.PlaidItem, userID uuid.UUID, accounts []plaidclient.Account) int {
	if len(accounts) == 0 {
		return 0
	}
	person, err := s.budgets.GetPersonByUserID(ctx, item.BudgetProfileID, userID)
	if err != nil {
		return 0
	}
	personID := int32(person.ID)

	created := 0
	for _, acct := range accounts {
		name := plaidclient.PlaidAccountName(acct.Name, acct.Mask)
		plaidAcctID := acct.PlaidAccountID

		// Exact match by plaid_account_id — same connection or stable ID.
		if _, existsErr := s.transactions.GetPaymentMethodByPlaidAccountID(ctx, plaidAcctID); existsErr == nil {
			continue
		}
		// Name-based fallback — Plaid issues new account_ids on reconnect.
		// If a method with the same name exists, update its plaid_account_id
		// so future reconnects dedup correctly, then skip creation.
		if existing, existsErr := s.transactions.GetPaymentMethodByUserAndName(ctx, userID, name); existsErr == nil {
			_ = s.transactions.UpdatePaymentMethodPlaidAccountID(ctx, existing.ID, plaidAcctID)
			continue
		}

		typeID := plaidclient.PlaidPaymentTypeID(acct.Type, acct.Subtype)
		if _, pmErr := s.transactions.CreatePaymentMethodFromPlaid(ctx, db.CreatePaymentMethodFromPlaidParams{
			Name:           name,
			PaymentTypeID:  &typeID,
			UserID:         &userID,
			BudgetPersonID: &personID,
			PlaidAccountID: &plaidAcctID,
			PlaidItemID:    &item.ID,
		}); pmErr == nil {
			log.Printf("plaid: created payment method %q (account %s)", name, plaidAcctID)
			created++
		}
	}
	return created
}

// RefreshAccounts re-fetches a connection's current account list from Plaid
// and reconciles payment_methods: creates one for any newly-added account,
// and deactivates any existing payment method for this connection whose
// account is no longer present. Called after a Link update-mode session
// completes (e.g. account selection) — update mode doesn't return a
// public_token, so there's nothing to exchange, only accounts to re-sync.
func (s *PlaidService) RefreshAccounts(ctx context.Context, userID, connectionID uuid.UUID) (db.PlaidItem, error) {
	if err := s.requireUS(ctx, userID); err != nil {
		return db.PlaidItem{}, err
	}
	item, err := s.items.GetByID(ctx, connectionID)
	if err != nil {
		return db.PlaidItem{}, err
	}
	if item.UserID != userID {
		return db.PlaidItem{}, apperr.Forbidden("access denied")
	}

	accessToken, err := crypto.Decrypt(item.AccessToken, s.encryptionKey)
	if err != nil {
		return db.PlaidItem{}, fmt.Errorf("plaid: decrypt access token: %w", err)
	}

	accounts, _, err := s.plaid.GetAccounts(ctx, accessToken)
	if err != nil {
		return db.PlaidItem{}, fmt.Errorf("plaid: get accounts: %w", err)
	}

	created := s.createMissingPaymentMethods(ctx, item, userID, accounts)

	stillPresent := make(map[string]bool, len(accounts))
	for _, acct := range accounts {
		stillPresent[acct.PlaidAccountID] = true
	}
	deactivated := 0
	existingMethods, err := s.transactions.ListActivePaymentMethodsByPlaidItem(ctx, item.ID)
	if err == nil {
		for _, pm := range existingMethods {
			if pm.PlaidAccountID == nil || stillPresent[*pm.PlaidAccountID] {
				continue
			}
			if deactivateErr := s.transactions.DeactivatePaymentMethod(ctx, pm.ID); deactivateErr == nil {
				log.Printf("plaid: refresh — deactivated payment method %q (account removed)", pm.Name)
				deactivated++
			}
		}
	}

	log.Printf("plaid: item %s refreshed — %d payment method(s) created, %d deactivated", item.ItemID, created, deactivated)
	return item, nil
}
