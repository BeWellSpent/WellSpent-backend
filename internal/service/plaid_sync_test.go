package service

import (
	"context"
	"errors"
	"testing"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/BeWellSpent/wellspent-backend/internal/crypto"
	plaidclient "github.com/BeWellSpent/wellspent-backend/internal/plaid"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func numericFromString(t *testing.T, s string) pgtype.Numeric {
	t.Helper()
	var n pgtype.Numeric
	require.NoError(t, n.Scan(s))
	return n
}

func makeFixedExpense(t *testing.T, id uuid.UUID, name, amount string) db.FixedExpense {
	t.Helper()
	return db.FixedExpense{
		ID:            id,
		Name:          name,
		PlannedAmount: numericFromString(t, amount),
	}
}

func TestSyncScoreBestMatch_AliasHitPicksAmountCompatibleCandidate(t *testing.T) {
	fe15ID := uuid.New()
	fe5ID := uuid.New()
	fe15 := makeFixedExpense(t, fe15ID, "Creator Support A", "15.00")
	fe5 := makeFixedExpense(t, fe5ID, "Creator Support B", "5.33")
	aliases := map[uuid.UUID][]string{
		fe15ID: {"Patreon"},
		fe5ID:  {"Patreon"},
	}

	tx := plaidclient.Transaction{Name: "Patreon", Amount: 5.33}
	score, bestFE, aliasHit, amountOK := syncScoreBestMatch(tx, nil, nil, []db.FixedExpense{fe15, fe5}, aliases)

	require.NotNil(t, bestFE)
	assert.Equal(t, fe5ID, bestFE.ID, "should resolve to the amount-matching template, not just the first alias hit")
	assert.True(t, aliasHit)
	assert.True(t, amountOK)
	assert.Equal(t, 60.0, score)
}

func TestSyncScoreBestMatch_AliasHitWithoutAmountMatchDoesNotReachAutoConfirmThreshold(t *testing.T) {
	feID := uuid.New()
	fe := makeFixedExpense(t, feID, "Creator Support", "15.00")
	aliases := map[uuid.UUID][]string{feID: {"Patreon"}}

	tx := plaidclient.Transaction{Name: "Patreon", Amount: 5.33}
	score, bestFE, aliasHit, amountOK := syncScoreBestMatch(tx, nil, nil, []db.FixedExpense{fe}, aliases)

	require.NotNil(t, bestFE)
	assert.True(t, aliasHit)
	assert.False(t, amountOK)
	assert.Less(t, score, 80.0)
}

func TestSyncScoreBestMatch_FallsBackToWordOverlapWithoutAlias(t *testing.T) {
	feID := uuid.New()
	fe := makeFixedExpense(t, feID, "Amex Renewal Membership", "150.00")
	tx := plaidclient.Transaction{Name: "RENEWAL MEMBERSHIP FEE", Amount: 150.00}

	score, bestFE, aliasHit, amountOK := syncScoreBestMatch(tx, nil, nil, []db.FixedExpense{fe}, nil)

	require.NotNil(t, bestFE)
	assert.False(t, aliasHit)
	assert.True(t, amountOK)
	assert.Equal(t, 60.0, score)
}

func TestSyncScoreBestMatch_NoCandidatesReturnsNil(t *testing.T) {
	tx := plaidclient.Transaction{Name: "Patreon", Amount: 5.33}
	score, bestFE, aliasHit, amountOK := syncScoreBestMatch(tx, nil, nil, nil, nil)

	assert.Nil(t, bestFE)
	assert.Equal(t, 0.0, score)
	assert.False(t, aliasHit)
	assert.False(t, amountOK)
}

func TestSyncAmountWithinTolerance(t *testing.T) {
	feID := uuid.New()
	fe := makeFixedExpense(t, feID, "Rent", "1000.00")

	assert.True(t, syncAmountWithinTolerance(1000.00, &fe))
	assert.True(t, syncAmountWithinTolerance(997.50, &fe), "within $3 tolerance")
	assert.False(t, syncAmountWithinTolerance(990.00, &fe), "outside $3 tolerance")
}

func TestSyncNameWordsOverlap(t *testing.T) {
	assert.True(t, syncNameWordsOverlap("renewal membership fee", "amex renewal"))
	assert.False(t, syncNameWordsOverlap("patreon", "creator support subscription"))
}

func TestSyncResolveCategory_PayrollNameOverridesPFCCategory(t *testing.T) {
	assert.Equal(t, "Income", syncResolveCategory("ACME CORP PAYROLL", "TRANSFER_IN", "TRANSFER_IN_DEPOSIT"))
	assert.Equal(t, "Income", syncResolveCategory("payroll deposit", "", ""))
	assert.Equal(t, "Income", syncResolveCategory("Bi-Weekly Payroll", "GENERAL_MERCHANDISE", "GENERAL_MERCHANDISE_PET_SUPPLIES"))
}

func TestSyncResolveCategory_NonPayrollFallsBackToPFCMapping(t *testing.T) {
	assert.Equal(t, "Groceries", syncResolveCategory("WHOLE FOODS", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES"))
	assert.Equal(t, "", syncResolveCategory("UNKNOWN MERCHANT", "", ""))
}

func TestSyncItem_ReturnsErrorWhenCursorPersistFails(t *testing.T) {
	itemID := uuid.New()
	profileID := uuid.New()
	encrypted, err := crypto.Encrypt("real-access-token", testEncKey)
	require.NoError(t, err)

	svc := NewPlaidService(
		&mockPlaidClient{
			syncTransactions: func(_ context.Context, _, _ string) ([]plaidclient.Transaction, []plaidclient.Transaction, []string, string, error) {
				return nil, nil, nil, "new-cursor", nil
			},
		},
		&mockPlaidRepo{
			updateSync: func(_ context.Context, _ db.UpdatePlaidItemSyncParams) (db.PlaidItem, error) {
				return db.PlaidItem{}, errors.New("connection reset")
			},
		},
		nil, // budgets — unreached: SyncTransactions returns no added transactions
		nil, // users — unreached: only used by SyncItem's callers, not SyncItem itself
		&mockTransactionRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
		testEncKey,
	)

	item := db.PlaidItem{ID: itemID, BudgetProfileID: profileID, AccessToken: encrypted}
	syncErr := svc.SyncItem(context.Background(), item)

	require.Error(t, syncErr, "a failure to persist the new cursor must surface as a sync failure, not be silently swallowed")
	assert.Contains(t, syncErr.Error(), "persist cursor")
}
