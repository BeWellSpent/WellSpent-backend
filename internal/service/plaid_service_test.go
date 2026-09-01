package service

import (
	"context"
	"testing"
	"time"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/crypto"
	plaidclient "github.com/BeWellSpent/wellspent-backend/internal/plaid"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// ── Mock Plaid client ─────────────────────────────────────────────────────────

type mockPlaidClient struct {
	createLinkToken     func(ctx context.Context, userID, updateAccessToken, redirectURI string) (string, string, error)
	exchangePublicToken func(ctx context.Context, publicToken string) (string, string, error)
	getAccounts         func(ctx context.Context, accessToken string) ([]plaidclient.Account, string, error)
	getInstitutionName  func(ctx context.Context, institutionID string) (string, error)
	removeItem          func(ctx context.Context, accessToken string) error
	syncTransactions    func(ctx context.Context, accessToken, cursor string) ([]plaidclient.Transaction, []plaidclient.Transaction, []string, string, error)
}

func (m *mockPlaidClient) CreateLinkToken(ctx context.Context, userID, updateAccessToken, redirectURI string) (string, string, error) {
	if m.createLinkToken != nil {
		return m.createLinkToken(ctx, userID, updateAccessToken, redirectURI)
	}
	return "link-token", "2099-01-01T00:00:00Z", nil
}

func (m *mockPlaidClient) ExchangePublicToken(ctx context.Context, publicToken string) (string, string, error) {
	if m.exchangePublicToken != nil {
		return m.exchangePublicToken(ctx, publicToken)
	}
	return "access-token", "item-id-123", nil
}

func (m *mockPlaidClient) GetAccounts(ctx context.Context, accessToken string) ([]plaidclient.Account, string, error) {
	if m.getAccounts != nil {
		return m.getAccounts(ctx, accessToken)
	}
	return nil, "", nil
}

func (m *mockPlaidClient) GetInstitutionName(ctx context.Context, institutionID string) (string, error) {
	if m.getInstitutionName != nil {
		return m.getInstitutionName(ctx, institutionID)
	}
	return "Test Bank", nil
}

func (m *mockPlaidClient) RemoveItem(ctx context.Context, accessToken string) error {
	if m.removeItem != nil {
		return m.removeItem(ctx, accessToken)
	}
	return nil
}

func (m *mockPlaidClient) SyncTransactions(ctx context.Context, accessToken, cursor string) ([]plaidclient.Transaction, []plaidclient.Transaction, []string, string, error) {
	if m.syncTransactions != nil {
		return m.syncTransactions(ctx, accessToken, cursor)
	}
	return nil, nil, nil, "", nil
}

// ── Mock repos (reuse mockUserRepo from auth_service_test.go) ─────────────────

type mockPlaidRepo struct {
	create             func(context.Context, db.CreatePlaidItemParams) (db.PlaidItem, error)
	getByID            func(context.Context, uuid.UUID) (db.PlaidItem, error)
	getByItemID        func(context.Context, string) (db.PlaidItem, error)
	listByUser         func(context.Context, uuid.UUID) ([]db.PlaidItem, error)
	listByProfile      func(context.Context, uuid.UUID) ([]db.PlaidItem, error)
	listWithOwner      func(context.Context, uuid.UUID) ([]db.ListActivePlaidItemsWithOwnerByBudgetProfileRow, error)
	listForSync        func(context.Context) ([]db.PlaidItem, error)
	listForProfileSync func(context.Context, uuid.UUID) ([]db.PlaidItem, error)
	listUnsyncable     func(context.Context, uuid.UUID) ([]db.ListUnsyncableConnectionsForUserRow, error)
	updateStatus       func(context.Context, db.UpdatePlaidItemStatusParams) (db.PlaidItem, error)
	updateSync         func(context.Context, db.UpdatePlaidItemSyncParams) (db.PlaidItem, error)
	resetCursor        func(context.Context, uuid.UUID) (db.PlaidItem, error)
	delete             func(context.Context, uuid.UUID) error
}

func (m *mockPlaidRepo) ListUnsyncableForUser(ctx context.Context, userID uuid.UUID) ([]db.ListUnsyncableConnectionsForUserRow, error) {
	if m.listUnsyncable != nil {
		return m.listUnsyncable(ctx, userID)
	}
	return nil, nil
}

func (m *mockPlaidRepo) Create(ctx context.Context, arg db.CreatePlaidItemParams) (db.PlaidItem, error) {
	if m.create != nil {
		return m.create(ctx, arg)
	}
	return db.PlaidItem{ID: uuid.New(), UserID: arg.UserID, BudgetProfileID: arg.BudgetProfileID, Status: "active"}, nil
}
func (m *mockPlaidRepo) GetByID(ctx context.Context, id uuid.UUID) (db.PlaidItem, error) {
	if m.getByID != nil {
		return m.getByID(ctx, id)
	}
	return db.PlaidItem{}, apperr.NotFound("plaid_item", id.String())
}
func (m *mockPlaidRepo) GetByItemID(ctx context.Context, itemID string) (db.PlaidItem, error) {
	if m.getByItemID != nil {
		return m.getByItemID(ctx, itemID)
	}
	return db.PlaidItem{}, apperr.NotFound("plaid_item", itemID)
}
func (m *mockPlaidRepo) ListByUser(ctx context.Context, userID uuid.UUID) ([]db.PlaidItem, error) {
	if m.listByUser != nil {
		return m.listByUser(ctx, userID)
	}
	return nil, nil
}
func (m *mockPlaidRepo) ListByBudgetProfile(ctx context.Context, profileID uuid.UUID) ([]db.PlaidItem, error) {
	if m.listByProfile != nil {
		return m.listByProfile(ctx, profileID)
	}
	return nil, nil
}
func (m *mockPlaidRepo) ListActiveWithOwnerByBudgetProfile(ctx context.Context, profileID uuid.UUID) ([]db.ListActivePlaidItemsWithOwnerByBudgetProfileRow, error) {
	if m.listWithOwner != nil {
		return m.listWithOwner(ctx, profileID)
	}
	return nil, nil
}
func (m *mockPlaidRepo) ResetCursor(ctx context.Context, id uuid.UUID) (db.PlaidItem, error) {
	if m.resetCursor != nil {
		return m.resetCursor(ctx, id)
	}
	return db.PlaidItem{ID: id, Status: "active"}, nil
}
func (m *mockPlaidRepo) ListActiveForSync(ctx context.Context) ([]db.PlaidItem, error) {
	if m.listForSync != nil {
		return m.listForSync(ctx)
	}
	return nil, nil
}
func (m *mockPlaidRepo) ListActiveForProfileSync(ctx context.Context, profileID uuid.UUID) ([]db.PlaidItem, error) {
	if m.listForProfileSync != nil {
		return m.listForProfileSync(ctx, profileID)
	}
	return nil, nil
}
func (m *mockPlaidRepo) UpdateStatus(ctx context.Context, arg db.UpdatePlaidItemStatusParams) (db.PlaidItem, error) {
	if m.updateStatus != nil {
		return m.updateStatus(ctx, arg)
	}
	return db.PlaidItem{ID: arg.ID, Status: arg.Status}, nil
}
func (m *mockPlaidRepo) UpdateSync(ctx context.Context, arg db.UpdatePlaidItemSyncParams) (db.PlaidItem, error) {
	if m.updateSync != nil {
		return m.updateSync(ctx, arg)
	}
	return db.PlaidItem{ID: arg.ID}, nil
}
func (m *mockPlaidRepo) Delete(ctx context.Context, id uuid.UUID) error {
	if m.delete != nil {
		return m.delete(ctx, id)
	}
	return nil
}

// ── Helpers ───────────────────────────────────────────────────────────────────

func usUser() db.User {
	cc := "US"
	return db.User{ID: uuid.New(), CountryCode: &cc}
}

func nonUSUser() db.User {
	cc := "AR"
	return db.User{ID: uuid.New(), CountryCode: &cc}
}

func freeUser() db.User {
	cc := "US"
	return db.User{ID: uuid.New(), CountryCode: &cc, Plan: "free"}
}

// testEncKey is a valid 64-char hex key used only in tests.
const testEncKey = "0000000000000000000000000000000000000000000000000000000000000000"

// ── Tests ─────────────────────────────────────────────────────────────────────

func TestPlaid_CreateLinkToken_Success(t *testing.T) {
	user := usUser()
	profileID := uuid.New()

	budgetRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: user.ID}, nil
		},
	}
	svc := NewPlaidService(&mockPlaidClient{}, &mockPlaidRepo{}, budgetRepo, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	result, err := svc.CreateLinkToken(context.Background(), user.ID, profileID, nil, "")
	require.NoError(t, err)
	assert.Equal(t, "link-token", result.LinkToken)
}

func TestPlaid_CreateLinkToken_ForwardsRedirectURI(t *testing.T) {
	user := usUser()
	profileID := uuid.New()
	var gotRedirectURI string

	budgetRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: user.ID}, nil
		},
	}
	plaidClient := &mockPlaidClient{
		createLinkToken: func(_ context.Context, _, _, redirectURI string) (string, string, error) {
			gotRedirectURI = redirectURI
			return "link-token", "2099-01-01T00:00:00Z", nil
		},
	}
	svc := NewPlaidService(plaidClient, &mockPlaidRepo{}, budgetRepo, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	_, err := svc.CreateLinkToken(context.Background(), user.ID, profileID, nil, "https://bewellspent.com/plaid-oauth-redirect")
	require.NoError(t, err)
	assert.Equal(t, "https://bewellspent.com/plaid-oauth-redirect", gotRedirectURI)
}

func TestPlaid_CreateLinkToken_OmitsRedirectURIWhenNotProvided(t *testing.T) {
	user := usUser()
	profileID := uuid.New()
	var gotRedirectURI string

	budgetRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: user.ID}, nil
		},
	}
	plaidClient := &mockPlaidClient{
		createLinkToken: func(_ context.Context, _, _, redirectURI string) (string, string, error) {
			gotRedirectURI = redirectURI
			return "link-token", "2099-01-01T00:00:00Z", nil
		},
	}
	svc := NewPlaidService(plaidClient, &mockPlaidRepo{}, budgetRepo, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	_, err := svc.CreateLinkToken(context.Background(), user.ID, profileID, nil, "")
	require.NoError(t, err)
	assert.Equal(t, "", gotRedirectURI)
}

func TestPlaid_CreateLinkToken_NonUS_Forbidden(t *testing.T) {
	user := nonUSUser()

	svc := NewPlaidService(&mockPlaidClient{}, &mockPlaidRepo{}, &mockBudgetProfileRepo{}, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	_, err := svc.CreateLinkToken(context.Background(), user.ID, uuid.New(), nil, "")
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	assert.ErrorAs(t, err, &forbidden)
}

func TestPlaid_CreateLinkToken_FreeTier_Invalid(t *testing.T) {
	user := freeUser()

	svc := NewPlaidService(&mockPlaidClient{}, &mockPlaidRepo{}, &mockBudgetProfileRepo{}, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	_, err := svc.CreateLinkToken(context.Background(), user.ID, uuid.New(), nil, "")
	require.Error(t, err)
	var invalid *apperr.ValidationError
	assert.ErrorAs(t, err, &invalid)
}

func TestPlaid_ExchangePublicToken_FreeTier_Invalid(t *testing.T) {
	user := freeUser()

	svc := NewPlaidService(&mockPlaidClient{}, &mockPlaidRepo{}, &mockBudgetProfileRepo{}, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	_, err := svc.ExchangePublicToken(context.Background(), user.ID, uuid.New(), "public-token-sandbox")
	require.Error(t, err)
	var invalid *apperr.ValidationError
	assert.ErrorAs(t, err, &invalid)
}

func TestPlaid_Disconnect_FreeTier_StillAllowed(t *testing.T) {
	user := freeUser()
	connID := uuid.New()
	statusUpdated := ""

	plaidRepo := &mockPlaidRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.PlaidItem, error) {
			return db.PlaidItem{ID: id, UserID: user.ID, AccessToken: "access-sandbox"}, nil
		},
		updateStatus: func(_ context.Context, arg db.UpdatePlaidItemStatusParams) (db.PlaidItem, error) {
			statusUpdated = arg.Status
			return db.PlaidItem{ID: arg.ID, Status: arg.Status}, nil
		},
	}
	svc := NewPlaidService(&mockPlaidClient{}, plaidRepo, &mockBudgetProfileRepo{}, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	// Disconnect must never be gated by plan — removing access should always
	// be possible, even for a free-tier user who somehow already has a
	// connection (e.g. was downgraded after linking while Pro).
	err := svc.Disconnect(context.Background(), user.ID, connID)
	require.NoError(t, err)
	assert.Equal(t, "disconnected", statusUpdated)
}

func TestPlaid_ExchangePublicToken_Success(t *testing.T) {
	user := usUser()
	profileID := uuid.New()

	budgetRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: user.ID}, nil
		},
	}
	svc := NewPlaidService(&mockPlaidClient{}, &mockPlaidRepo{}, budgetRepo, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	item, err := svc.ExchangePublicToken(context.Background(), user.ID, profileID, "public-token-sandbox")
	require.NoError(t, err)
	assert.Equal(t, user.ID, item.UserID)
	assert.Equal(t, profileID, item.BudgetProfileID)
	assert.Equal(t, "active", item.Status)
}

func TestPlaid_Disconnect_Success(t *testing.T) {
	user := usUser()
	connID := uuid.New()
	statusUpdated := ""

	plaidRepo := &mockPlaidRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.PlaidItem, error) {
			return db.PlaidItem{ID: id, UserID: user.ID, AccessToken: "access-sandbox"}, nil
		},
		updateStatus: func(_ context.Context, arg db.UpdatePlaidItemStatusParams) (db.PlaidItem, error) {
			statusUpdated = arg.Status
			return db.PlaidItem{ID: arg.ID, Status: arg.Status}, nil
		},
	}
	svc := NewPlaidService(&mockPlaidClient{}, plaidRepo, &mockBudgetProfileRepo{}, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	err := svc.Disconnect(context.Background(), user.ID, connID)
	require.NoError(t, err)
	assert.Equal(t, "disconnected", statusUpdated)
}

func TestPlaid_Disconnect_WrongUser_Forbidden(t *testing.T) {
	user := usUser()
	otherUserID := uuid.New()
	connID := uuid.New()

	plaidRepo := &mockPlaidRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.PlaidItem, error) {
			return db.PlaidItem{ID: id, UserID: otherUserID}, nil
		},
	}
	svc := NewPlaidService(&mockPlaidClient{}, plaidRepo, &mockBudgetProfileRepo{}, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	err := svc.Disconnect(context.Background(), user.ID, connID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	assert.ErrorAs(t, err, &forbidden)
}

// ── CreateLinkToken update mode ─────────────────────────────────────────────

func TestPlaid_CreateLinkToken_UpdateMode_PassesDecryptedAccessToken(t *testing.T) {
	user := usUser()
	connID := uuid.New()
	profileID := uuid.New()
	encrypted, err := crypto.Encrypt("real-access-token", testEncKey)
	require.NoError(t, err)

	var gotAccessToken string
	plaidRepo := &mockPlaidRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.PlaidItem, error) {
			return db.PlaidItem{ID: id, UserID: user.ID, AccessToken: encrypted}, nil
		},
	}
	plaidClient := &mockPlaidClient{
		createLinkToken: func(_ context.Context, _, updateAccessToken, _ string) (string, string, error) {
			gotAccessToken = updateAccessToken
			return "update-link-token", "2099-01-01T00:00:00Z", nil
		},
	}
	budgetRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: user.ID}, nil
		},
	}
	svc := NewPlaidService(plaidClient, plaidRepo, budgetRepo, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	result, err := svc.CreateLinkToken(context.Background(), user.ID, profileID, &connID, "")
	require.NoError(t, err)
	assert.Equal(t, "update-link-token", result.LinkToken)
	assert.Equal(t, "real-access-token", gotAccessToken)
}

func TestPlaid_CreateLinkToken_UpdateMode_WrongUser_Forbidden(t *testing.T) {
	user := usUser()
	otherUserID := uuid.New()
	connID := uuid.New()
	profileID := uuid.New()

	plaidRepo := &mockPlaidRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.PlaidItem, error) {
			return db.PlaidItem{ID: id, UserID: otherUserID}, nil
		},
	}
	budgetRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: user.ID}, nil
		},
	}
	svc := NewPlaidService(&mockPlaidClient{}, plaidRepo, budgetRepo, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	_, err := svc.CreateLinkToken(context.Background(), user.ID, profileID, &connID, "")
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	assert.ErrorAs(t, err, &forbidden)
}

// ── RefreshAccounts ──────────────────────────────────────────────────────────

func TestPlaid_RefreshAccounts_CreatesNewAndDeactivatesRemoved(t *testing.T) {
	user := usUser()
	profileID := uuid.New()
	itemID := uuid.New()
	encrypted, err := crypto.Encrypt("real-access-token", testEncKey)
	require.NoError(t, err)

	// Plaid now reports only "acct-kept" — "acct-removed" (an existing
	// payment method) is gone, and "acct-new" wasn't there before.
	plaidClient := &mockPlaidClient{
		getAccounts: func(_ context.Context, _ string) ([]plaidclient.Account, string, error) {
			return []plaidclient.Account{
				{PlaidAccountID: "acct-kept", Name: "Checking", Type: "depository", Subtype: "checking"},
				{PlaidAccountID: "acct-new", Name: "Savings", Type: "depository", Subtype: "savings"},
			}, "inst-1", nil
		},
	}
	plaidRepo := &mockPlaidRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.PlaidItem, error) {
			return db.PlaidItem{ID: id, UserID: user.ID, BudgetProfileID: profileID, AccessToken: encrypted, ItemID: "plaid-item-1"}, nil
		},
	}
	budgetRepo := &mockBudgetProfileRepo{
		getPersonByUserID: func(_ context.Context, _, _ uuid.UUID) (db.BudgetToProfileMapping, error) {
			return db.BudgetToProfileMapping{ID: 1}, nil
		},
	}

	var createdNames []string
	var deactivatedIDs []uuid.UUID
	txRepo := &mockTransactionRepo{
		getPaymentMethodByPlaidAccountID: func(_ context.Context, plaidAccountID string) (db.PaymentMethod, error) {
			if plaidAccountID == "acct-kept" {
				return db.PaymentMethod{ID: uuid.New(), PlaidAccountID: &plaidAccountID}, nil
			}
			return db.PaymentMethod{}, apperr.NotFound("payment_method", plaidAccountID)
		},
		getPaymentMethodByUserAndName: func(_ context.Context, _ uuid.UUID, _ string) (db.PaymentMethod, error) {
			return db.PaymentMethod{}, apperr.NotFound("payment_method", "name")
		},
		createPaymentMethodFromPlaid: func(_ context.Context, arg db.CreatePaymentMethodFromPlaidParams) (db.PaymentMethod, error) {
			createdNames = append(createdNames, arg.Name)
			return db.PaymentMethod{ID: uuid.New(), Name: arg.Name, PlaidAccountID: arg.PlaidAccountID}, nil
		},
		listActivePaymentMethodsByPlaidItem: func(_ context.Context, _ uuid.UUID) ([]db.PaymentMethod, error) {
			removedAcctID := "acct-removed"
			keptAcctID := "acct-kept"
			return []db.PaymentMethod{
				{ID: uuid.New(), Name: "Old Removed Account", PlaidAccountID: &removedAcctID},
				{ID: itemID, Name: "Checking", PlaidAccountID: &keptAcctID},
			}, nil
		},
		deactivatePaymentMethod: func(_ context.Context, id uuid.UUID) error {
			deactivatedIDs = append(deactivatedIDs, id)
			return nil
		},
	}

	svc := NewPlaidService(plaidClient, plaidRepo, budgetRepo, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, txRepo, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	item, err := svc.RefreshAccounts(context.Background(), user.ID, uuid.New())
	require.NoError(t, err)
	assert.Equal(t, "plaid-item-1", item.ItemID)

	require.Len(t, createdNames, 1, "should create exactly one method, for the new account")
	assert.Equal(t, "Savings", createdNames[0])

	require.Len(t, deactivatedIDs, 1, "should deactivate exactly the removed account's method")
}

func TestPlaid_RefreshAccounts_WrongUser_Forbidden(t *testing.T) {
	user := usUser()
	otherUserID := uuid.New()
	connID := uuid.New()

	plaidRepo := &mockPlaidRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.PlaidItem, error) {
			return db.PlaidItem{ID: id, UserID: otherUserID}, nil
		},
	}
	svc := NewPlaidService(&mockPlaidClient{}, plaidRepo, &mockBudgetProfileRepo{}, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	_, err := svc.RefreshAccounts(context.Background(), user.ID, connID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	assert.ErrorAs(t, err, &forbidden)
}

// ── Budget-scoped connection list ─────────────────────────────────────────────

func TestPlaid_GetConnections_ByBudget_MarksOwnershipAndEntitlement(t *testing.T) {
	caller := usUser()
	profileID := uuid.New()
	otherUserID := uuid.New()

	budgetRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: caller.ID}, nil
		},
	}
	plaidRepo := &mockPlaidRepo{
		listWithOwner: func(_ context.Context, _ uuid.UUID) ([]db.ListActivePlaidItemsWithOwnerByBudgetProfileRow, error) {
			return []db.ListActivePlaidItemsWithOwnerByBudgetProfileRow{
				{ID: uuid.New(), UserID: caller.ID, Status: "active", OwnerName: "Ada Lovelace", OwnerPlan: "lifetime"},
				{ID: uuid.New(), UserID: otherUserID, Status: "active", OwnerName: "Grace Hopper", OwnerPlan: "free"},
			}, nil
		},
	}
	svc := NewPlaidService(&mockPlaidClient{}, plaidRepo, budgetRepo, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return caller, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	views, err := svc.GetConnections(context.Background(), caller.ID, &profileID)
	require.NoError(t, err)
	require.Len(t, views, 2)

	assert.True(t, views[0].IsOwner)
	assert.Equal(t, "Ada Lovelace", views[0].OwnerName)
	assert.True(t, views[0].SyncEnabled)

	// A co-member's connection: visible, attributed, but not actionable — and
	// flagged as never syncing, which `status` alone would report as healthy.
	assert.False(t, views[1].IsOwner)
	assert.Equal(t, "Grace Hopper", views[1].OwnerName)
	assert.False(t, views[1].SyncEnabled)
}

func TestPlaid_GetConnections_ByBudget_NotAMember_Forbidden(t *testing.T) {
	caller := usUser()
	profileID := uuid.New()

	budgetRepo := &mockBudgetProfileRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: id, UserID: uuid.New()}, nil
		},
		existsPersonForUser: func(_ context.Context, _, _ uuid.UUID) (bool, error) { return false, nil },
	}
	svc := NewPlaidService(&mockPlaidClient{}, &mockPlaidRepo{}, budgetRepo, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return caller, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)

	_, err := svc.GetConnections(context.Background(), caller.ID, &profileID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	assert.ErrorAs(t, err, &forbidden)
}

// ── Resync ────────────────────────────────────────────────────────────────────

// resyncSvc wires a service whose GetByID returns `item` and whose user lookup
// returns `user`, capturing whether the cursor was actually reset.
func resyncSvc(item db.PlaidItem, user db.User, reset *bool) *PlaidService {
	plaidRepo := &mockPlaidRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.PlaidItem, error) { return item, nil },
		resetCursor: func(_ context.Context, id uuid.UUID) (db.PlaidItem, error) {
			*reset = true
			return db.PlaidItem{ID: id, UserID: item.UserID, Status: "active"}, nil
		},
	}
	return NewPlaidService(&mockPlaidClient{}, plaidRepo, &mockBudgetProfileRepo{}, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, &mockTransactionRepo{}, &mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey)
}

func TestPlaid_Resync_Success(t *testing.T) {
	user := usUser()
	user.Plan = "lifetime"
	item := db.PlaidItem{ID: uuid.New(), UserID: user.ID, Status: "active"}

	reset := false
	svc := resyncSvc(item, user, &reset)

	view, err := svc.ResyncConnection(context.Background(), user.ID, item.ID)
	require.NoError(t, err)
	assert.True(t, reset, "expected the cursor to be cleared")
	assert.True(t, view.IsOwner)
	assert.Nil(t, view.ResyncAvailableAt, "a fresh resync response reports no pending cooldown")
}

func TestPlaid_Resync_WrongUser_Forbidden(t *testing.T) {
	user := usUser()
	user.Plan = "lifetime"
	item := db.PlaidItem{ID: uuid.New(), UserID: uuid.New(), Status: "active"}

	reset := false
	svc := resyncSvc(item, user, &reset)

	_, err := svc.ResyncConnection(context.Background(), user.ID, item.ID)
	var forbidden *apperr.ForbiddenError
	assert.ErrorAs(t, err, &forbidden)
	assert.False(t, reset, "a non-owner must not clear anyone's cursor")
}

func TestPlaid_Resync_FreeTier_Invalid(t *testing.T) {
	user := freeUser()
	item := db.PlaidItem{ID: uuid.New(), UserID: user.ID, Status: "active"}

	reset := false
	svc := resyncSvc(item, user, &reset)

	_, err := svc.ResyncConnection(context.Background(), user.ID, item.ID)
	require.Error(t, err)
	// Clearing the cursor for an owner the sync job skips would throw away the
	// user's place in the feed and import nothing in exchange.
	assert.False(t, reset, "an unentitled connection must keep its cursor")
}

func TestPlaid_Resync_Disconnected_Invalid(t *testing.T) {
	user := usUser()
	user.Plan = "pro"
	item := db.PlaidItem{ID: uuid.New(), UserID: user.ID, Status: "disconnected"}

	reset := false
	svc := resyncSvc(item, user, &reset)

	_, err := svc.ResyncConnection(context.Background(), user.ID, item.ID)
	require.Error(t, err)
	assert.False(t, reset)
}

func TestPlaid_Resync_WithinCooldown_Invalid(t *testing.T) {
	user := usUser()
	user.Plan = "pro"
	item := db.PlaidItem{
		ID:                 uuid.New(),
		UserID:             user.ID,
		Status:             "active",
		LastManualResyncAt: pgtype.Timestamptz{Time: time.Now().Add(-time.Hour), Valid: true},
	}

	reset := false
	svc := resyncSvc(item, user, &reset)

	_, err := svc.ResyncConnection(context.Background(), user.ID, item.ID)
	require.Error(t, err)
	assert.False(t, reset, "a second resync inside the cooldown must not replay the history again")
}

func TestPlaid_Resync_AfterCooldownElapsed_Allowed(t *testing.T) {
	user := usUser()
	user.Plan = "pro"
	item := db.PlaidItem{
		ID:                 uuid.New(),
		UserID:             user.ID,
		Status:             "active",
		LastManualResyncAt: pgtype.Timestamptz{Time: time.Now().Add(-manualResyncCooldown - time.Minute), Valid: true},
	}

	reset := false
	svc := resyncSvc(item, user, &reset)

	_, err := svc.ResyncConnection(context.Background(), user.ID, item.ID)
	require.NoError(t, err)
	assert.True(t, reset)
}

func TestResyncAvailableAt_NeverResyncedIsAllowedNow(t *testing.T) {
	assert.Nil(t, resyncAvailableAt(db.PlaidItem{}, time.Now()))
}

func TestResyncAvailableAt_ReportsExactUnlockTime(t *testing.T) {
	last := time.Date(2026, 8, 13, 9, 0, 0, 0, time.UTC)
	item := db.PlaidItem{LastManualResyncAt: pgtype.Timestamptz{Time: last, Valid: true}}

	next := resyncAvailableAt(item, last.Add(time.Hour))
	require.NotNil(t, next)
	assert.Equal(t, last.Add(manualResyncCooldown), *next)

	// Exactly at the boundary the cooldown is over, not still running.
	assert.Nil(t, resyncAvailableAt(item, last.Add(manualResyncCooldown)))
}
