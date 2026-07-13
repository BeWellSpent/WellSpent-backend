package service

import (
	"context"
	"testing"

	"github.com/google/uuid"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	plaidclient "github.com/mauro-afa91/spendsense/internal/plaid"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// ── Mock Plaid client ─────────────────────────────────────────────────────────

type mockPlaidClient struct {
	createLinkToken      func(ctx context.Context, userID string) (string, string, error)
	exchangePublicToken  func(ctx context.Context, publicToken string) (string, string, error)
	getInstitutionName   func(ctx context.Context, institutionID string) (string, error)
	removeItem           func(ctx context.Context, accessToken string) error
	syncTransactions     func(ctx context.Context, accessToken, cursor string) ([]plaidclient.Transaction, []plaidclient.Transaction, []string, string, error)
}

func (m *mockPlaidClient) CreateLinkToken(ctx context.Context, userID string) (string, string, error) {
	if m.createLinkToken != nil {
		return m.createLinkToken(ctx, userID)
	}
	return "link-token", "2099-01-01T00:00:00Z", nil
}

func (m *mockPlaidClient) ExchangePublicToken(ctx context.Context, publicToken string) (string, string, error) {
	if m.exchangePublicToken != nil {
		return m.exchangePublicToken(ctx, publicToken)
	}
	return "access-token", "item-id-123", nil
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
	create         func(context.Context, db.CreatePlaidItemParams) (db.PlaidItem, error)
	getByID        func(context.Context, uuid.UUID) (db.PlaidItem, error)
	getByItemID    func(context.Context, string) (db.PlaidItem, error)
	listByUser     func(context.Context, uuid.UUID) ([]db.PlaidItem, error)
	listByProfile  func(context.Context, uuid.UUID) ([]db.PlaidItem, error)
	listForSync    func(context.Context) ([]db.PlaidItem, error)
	updateStatus   func(context.Context, db.UpdatePlaidItemStatusParams) (db.PlaidItem, error)
	updateSync     func(context.Context, db.UpdatePlaidItemSyncParams) (db.PlaidItem, error)
	delete         func(context.Context, uuid.UUID) error
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
func (m *mockPlaidRepo) ListActiveForSync(ctx context.Context) ([]db.PlaidItem, error) {
	if m.listForSync != nil {
		return m.listForSync(ctx)
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

// testEncKey is a valid 64-char hex key used only in tests.
const testEncKey = "0000000000000000000000000000000000000000000000000000000000000000"

func newPlaidSvc(pc plaidclient.Client, plaidRepo *mockPlaidRepo, budgetRepo *mockBudgetProfileRepo) *PlaidService {
	return NewPlaidService(pc, plaidRepo, budgetRepo, &mockUserRepo{
		getByID: func(_ context.Context, id uuid.UUID) (db.User, error) {
			if plaidRepo.getByID == nil {
				return usUser(), nil
			}
			return usUser(), nil
		},
	}, testEncKey)
}

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
	}, testEncKey)

	result, err := svc.CreateLinkToken(context.Background(), user.ID, profileID)
	require.NoError(t, err)
	assert.Equal(t, "link-token", result.LinkToken)
}

func TestPlaid_CreateLinkToken_NonUS_Forbidden(t *testing.T) {
	user := nonUSUser()

	svc := NewPlaidService(&mockPlaidClient{}, &mockPlaidRepo{}, &mockBudgetProfileRepo{}, &mockUserRepo{
		getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) { return user, nil },
	}, testEncKey)

	_, err := svc.CreateLinkToken(context.Background(), user.ID, uuid.New())
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	assert.ErrorAs(t, err, &forbidden)
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
	}, testEncKey)

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
	}, testEncKey)

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
	}, testEncKey)

	err := svc.Disconnect(context.Background(), user.ID, connID)
	require.Error(t, err)
	var forbidden *apperr.ForbiddenError
	assert.ErrorAs(t, err, &forbidden)
}
