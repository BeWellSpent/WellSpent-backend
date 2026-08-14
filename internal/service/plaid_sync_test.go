package service

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
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

func TestSyncResolveCategory_IncomePFCPrimaryResolvesWithoutPayrollInName(t *testing.T) {
	// A direct-deposit paycheck whose name never says "payroll" (e.g. the
	// employer's legal entity name) must still resolve to Income via Plaid's
	// own personal_finance_category classification, not just the name check.
	assert.Equal(t, "Income", syncResolveCategory("ACME CORP DIRECT DEP", "INCOME", "INCOME_WAGES"))
	assert.Equal(t, "Income", syncResolveCategory("IRS TREAS 310 TAX REF", "INCOME", "INCOME_TAX_REFUND"))
}

func TestSyncResolveCategoryID_ResolvesToKnownID(t *testing.T) {
	categoryIDs := map[string]int32{"Shopping": 7}
	name, id := syncResolveCategoryID("AMAZON.COM", "GENERAL_MERCHANDISE", "GENERAL_MERCHANDISE_ONLINE_MARKETPLACES", categoryIDs)
	assert.Equal(t, "Shopping", name)
	require.NotNil(t, id)
	assert.Equal(t, int32(7), *id)
}

func TestSyncResolveCategoryID_UnmappedNameReturnsNilID(t *testing.T) {
	// Resolves to "Shopping" but the system-category map doesn't have it —
	// this is exactly the scenario that silently drops the category: the
	// transaction still imports, just with category_id NULL.
	categoryIDs := map[string]int32{"Groceries": 3}
	name, id := syncResolveCategoryID("AMAZON.COM", "GENERAL_MERCHANDISE", "GENERAL_MERCHANDISE_ONLINE_MARKETPLACES", categoryIDs)
	assert.Equal(t, "Shopping", name)
	assert.Nil(t, id)
}

func TestSyncResolveCategoryID_NoResolvedNameReturnsEmpty(t *testing.T) {
	name, id := syncResolveCategoryID("UNKNOWN MERCHANT", "", "", map[string]int32{})
	assert.Equal(t, "", name)
	assert.Nil(t, id)
}

func TestSyncCategoryLogValue(t *testing.T) {
	id := int32(7)
	assert.Equal(t, `"Shopping"`, syncCategoryLogValue("Shopping", &id))
	assert.Equal(t, `"Shopping" (unmapped — no matching system category, imported without a category)`, syncCategoryLogValue("Shopping", nil))
	assert.Equal(t, "none", syncCategoryLogValue("", nil))
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
		&mockBudgetProfileRepo{}, // budgets — unreached: SyncTransactions returns no added transactions
		&mockUserRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
				return db.User{Plan: "pro"}, nil
			},
		},
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

func TestSyncItem_AutoConfirmedMatch_ExcludesInsteadOfDeleting(t *testing.T) {
	itemID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	feID := uuid.New()
	unpaidTxID := uuid.New()
	insertedTxID := uuid.New()
	encrypted, err := crypto.Encrypt("real-access-token", testEncKey)
	require.NoError(t, err)

	var deleteCalled bool
	var excludedID uuid.UUID
	var excludedFlag bool
	var markedPaidID uuid.UUID
	var confirmedReviewID uuid.UUID
	var confirmedStatus string

	svc := NewPlaidService(
		&mockPlaidClient{
			syncTransactions: func(_ context.Context, _, _ string) ([]plaidclient.Transaction, []plaidclient.Transaction, []string, string, error) {
				return []plaidclient.Transaction{
					{PlaidID: "plaid-tx-1", Name: "Patreon", Amount: 15.00, Date: time.Now()},
				}, nil, nil, "new-cursor", nil
			},
		},
		&mockPlaidRepo{},
		&mockBudgetProfileRepo{
			getPeriodByDate: func(_ context.Context, _ uuid.UUID, _ pgtype.Date) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID}, nil
			},
		},
		&mockUserRepo{
				getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
					return db.User{Plan: "pro"}, nil
				},
			},
		&mockTransactionRepo{
			createTransactionFromPlaid: func(_ context.Context, _ db.CreateTransactionFromPlaidParams) (db.Transaction, error) {
				return db.Transaction{ID: insertedTxID}, nil
			},
			markAsPaid: func(_ context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				markedPaidID = arg.ID
				return db.Transaction{ID: arg.ID}, nil
			},
			setExcluded: func(_ context.Context, arg db.SetTransactionExcludedParams) (db.Transaction, error) {
				excludedID = arg.ID
				excludedFlag = arg.Excluded
				return db.Transaction{ID: arg.ID, IsExcluded: arg.Excluded}, nil
			},
			delete: func(_ context.Context, _ db.DeleteTransactionParams) error {
				deleteCalled = true
				return nil
			},
		},
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return []db.FixedExpense{makeFixedExpense(t, feID, "Creator Support", "15.00")}, nil
			},
			getUnpaidTransactionInPer: func(_ context.Context, arg db.GetUnpaidTransactionByFixedExpenseInPeriodParams) (db.Transaction, error) {
				// Scoped to the imported transaction's own period (issue #41).
				if arg.BudgetPeriodID != periodID {
					return db.Transaction{}, apperr.NotFound("transaction", arg.FixedExpenseID.String())
				}
				return db.Transaction{ID: unpaidTxID, BudgetPeriodID: &periodID, PlannedAmount: numericFromString(t, "15.00")}, nil
			},
		},
		&mockTransactionReviewRepo{
			listAliases: func(_ context.Context, _ uuid.UUID) ([]string, error) {
				return []string{"Patreon"}, nil
			},
			create: func(_ context.Context, _, transactionID, matchedTransactionID uuid.UUID, _ float64) (db.TransactionReview, error) {
				reviewID := uuid.New()
				confirmedReviewID = reviewID
				return db.TransactionReview{ID: reviewID, TransactionID: transactionID, MatchedTransactionID: matchedTransactionID}, nil
			},
			updateStatus: func(_ context.Context, id uuid.UUID, status string) error {
				assert.Equal(t, confirmedReviewID, id, "should confirm the review just created for this auto-match")
				confirmedStatus = status
				return nil
			},
		},
		testEncKey,
	)

	item := db.PlaidItem{ID: itemID, BudgetProfileID: profileID, AccessToken: encrypted}
	syncErr := svc.SyncItem(context.Background(), item)
	require.NoError(t, syncErr)

	assert.False(t, deleteCalled, "the auto-matched imported transaction must not be deleted")
	assert.Equal(t, insertedTxID, excludedID, "should exclude the imported transaction, not the matched fixed one")
	assert.True(t, excludedFlag)
	assert.Equal(t, unpaidTxID, markedPaidID, "should mark the matched fixed transaction paid")
	assert.Equal(t, "confirmed", confirmedStatus, "should record the auto-match as a confirmed review so unmarking paid can find and undo it")
}

// TestSyncItem_NotifiesOnceForPlainImports_NotOncePerTransaction covers the
// most common sync outcome — several imported transactions that don't match
// any fixed expense at all — and asserts exactly one new_transaction
// notification fires for the whole run, not one per row.
func TestSyncItem_NotifiesOnceForPlainImports_NotOncePerTransaction(t *testing.T) {
	itemID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	encrypted, err := crypto.Encrypt("real-access-token", testEncKey)
	require.NoError(t, err)

	var createCount int
	var lastBody string
	notifRepo := &mockNotifRepo{
		getBudgetSubscribers: func(_ context.Context, _ uuid.UUID, alertType string) ([]db.AlertSubscription, error) {
			if alertType == "new_transaction" {
				return []db.AlertSubscription{{ID: uuid.New(), UserID: uuid.New(), AlertType: "new_transaction", Channel: "in_app"}}, nil
			}
			return nil, nil
		},
		create: func(_ context.Context, arg db.CreateNotificationParams) (db.Notification, error) {
			createCount++
			lastBody = arg.Body
			return db.Notification{ID: uuid.New()}, nil
		},
	}
	notifSvc := newTestNotifSvc(notifRepo, &mockBudgetProfileRepo{})

	svc := NewPlaidService(
		&mockPlaidClient{
			syncTransactions: func(_ context.Context, _, _ string) ([]plaidclient.Transaction, []plaidclient.Transaction, []string, string, error) {
				return []plaidclient.Transaction{
					{PlaidID: "plaid-tx-1", Name: "Whole Foods", Amount: 42.10, Date: time.Now()},
					{PlaidID: "plaid-tx-2", Name: "Shell Gas", Amount: 30.00, Date: time.Now()},
					{PlaidID: "plaid-tx-3", Name: "Amazon", Amount: 19.99, Date: time.Now()},
				}, nil, nil, "new-cursor", nil
			},
		},
		&mockPlaidRepo{},
		&mockBudgetProfileRepo{
			getPeriodByDate: func(_ context.Context, _ uuid.UUID, _ pgtype.Date) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID}, nil
			},
		},
		&mockUserRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
				return db.User{Plan: "pro"}, nil
			},
		},
		&mockTransactionRepo{
			createTransactionFromPlaid: func(_ context.Context, _ db.CreateTransactionFromPlaidParams) (db.Transaction, error) {
				return db.Transaction{ID: uuid.New()}, nil
			},
		},
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return nil, nil // no fixed expenses to match against — every import is "plain"
			},
		},
		&mockTransactionReviewRepo{},
		testEncKey,
	).WithNotifications(notifSvc)

	item := db.PlaidItem{ID: itemID, BudgetProfileID: profileID, AccessToken: encrypted}
	require.NoError(t, svc.SyncItem(context.Background(), item))

	require.Equal(t, 1, createCount, "3 plain imports should still produce exactly one aggregated notification")
	assert.Contains(t, lastBody, "3", "the aggregated notification should mention the actual count")
}

// TestSyncItem_NotifiesOnceForQueuedReviews_NotOncePerTransaction mirrors the
// plain-import case above but for transactions that score high enough
// against a fixed expense to be queued for review (not auto-confirmed).
func TestSyncItem_NotifiesOnceForQueuedReviews_NotOncePerTransaction(t *testing.T) {
	itemID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	feID := uuid.New()
	unpaidTxID := uuid.New()
	pmID := uuid.New()
	encrypted, err := crypto.Encrypt("real-access-token", testEncKey)
	require.NoError(t, err)

	var createCount int
	var lastBody string
	notifRepo := &mockNotifRepo{
		getBudgetSubscribers: func(_ context.Context, _ uuid.UUID, alertType string) ([]db.AlertSubscription, error) {
			if alertType == "review_pending" {
				return []db.AlertSubscription{{ID: uuid.New(), UserID: uuid.New(), AlertType: "review_pending", Channel: "in_app"}}, nil
			}
			return nil, nil
		},
		create: func(_ context.Context, arg db.CreateNotificationParams) (db.Notification, error) {
			createCount++
			lastBody = arg.Body
			return db.Notification{ID: uuid.New()}, nil
		},
	}
	notifSvc := newTestNotifSvc(notifRepo, &mockBudgetProfileRepo{})

	svc := NewPlaidService(
		&mockPlaidClient{
			syncTransactions: func(_ context.Context, _, _ string) ([]plaidclient.Transaction, []plaidclient.Transaction, []string, string, error) {
				// Amount match (40) + word-overlap name match, no alias (20) +
				// payment method match (20) = 80 — reaches the review queue
				// threshold without an alias hit, so it lands in the "queued"
				// branch rather than auto-confirm.
				return []plaidclient.Transaction{
					{PlaidID: "plaid-tx-1", Name: "Creator Support Membership", Amount: 15.00, Date: time.Now(), AccountID: "acct-1"},
					{PlaidID: "plaid-tx-2", Name: "Creator Support Membership", Amount: 15.00, Date: time.Now(), AccountID: "acct-1"},
				}, nil, nil, "new-cursor", nil
			},
		},
		&mockPlaidRepo{},
		&mockBudgetProfileRepo{
			getPeriodByDate: func(_ context.Context, _ uuid.UUID, _ pgtype.Date) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID}, nil
			},
		},
		&mockUserRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
				return db.User{Plan: "pro"}, nil
			},
		},
		&mockTransactionRepo{
			createTransactionFromPlaid: func(_ context.Context, _ db.CreateTransactionFromPlaidParams) (db.Transaction, error) {
				return db.Transaction{ID: uuid.New()}, nil
			},
			getPaymentMethodByPlaidAccountID: func(_ context.Context, _ string) (db.PaymentMethod, error) {
				return db.PaymentMethod{ID: pmID}, nil
			},
		},
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				fe := makeFixedExpense(t, feID, "Creator Support", "15.00")
				fe.PaymentMethodID = &pmID
				return []db.FixedExpense{fe}, nil
			},
			getUnpaidTransactionInPer: func(_ context.Context, arg db.GetUnpaidTransactionByFixedExpenseInPeriodParams) (db.Transaction, error) {
				// Scoped to the imported transaction's own period (issue #41).
				if arg.BudgetPeriodID != periodID {
					return db.Transaction{}, apperr.NotFound("transaction", arg.FixedExpenseID.String())
				}
				return db.Transaction{ID: unpaidTxID, BudgetPeriodID: &periodID, PlannedAmount: numericFromString(t, "15.00")}, nil
			},
		},
		&mockTransactionReviewRepo{
			create: func(_ context.Context, _, transactionID, matchedTransactionID uuid.UUID, _ float64) (db.TransactionReview, error) {
				return db.TransactionReview{ID: uuid.New(), TransactionID: transactionID, MatchedTransactionID: matchedTransactionID}, nil
			},
		},
		testEncKey,
	).WithNotifications(notifSvc)

	item := db.PlaidItem{ID: itemID, BudgetProfileID: profileID, AccessToken: encrypted}
	require.NoError(t, svc.SyncItem(context.Background(), item))

	require.Equal(t, 1, createCount, "2 queued-for-review imports should still produce exactly one aggregated notification")
	assert.Contains(t, lastBody, "2", "the aggregated notification should mention the actual count")
}

// ── SyncAll: grouping, entitlement reporting, per-account attribution ─────────

// syncAllSvc builds a service whose Plaid client returns `added` for every
// item, and whose users all sit on the given plan.
func syncAllSvc(t *testing.T, plan string, items []db.PlaidItem, added []plaidclient.Transaction) *PlaidService {
	t.Helper()
	return NewPlaidService(
		&mockPlaidClient{
			syncTransactions: func(_ context.Context, _, _ string) ([]plaidclient.Transaction, []plaidclient.Transaction, []string, string, error) {
				return added, nil, nil, "next-cursor", nil
			},
		},
		&mockPlaidRepo{
			listForSync: func(_ context.Context) ([]db.PlaidItem, error) { return items, nil },
		},
		&mockBudgetProfileRepo{
			getPeriodByDate: func(_ context.Context, profileID uuid.UUID, _ pgtype.Date) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: uuid.New(), BudgetProfileID: profileID}, nil
			},
		},
		&mockUserRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
				return db.User{Plan: plan}, nil
			},
		},
		&mockTransactionRepo{},
		&mockFixedExpenseRepo{},
		&mockTransactionReviewRepo{},
		testEncKey,
	)
}

func encryptedToken(t *testing.T) string {
	t.Helper()
	enc, err := crypto.Encrypt("real-access-token", testEncKey)
	require.NoError(t, err)
	return enc
}

func TestSyncAll_GroupsConnectionsByBudgetProfile(t *testing.T) {
	profileA, profileB := uuid.New(), uuid.New()
	token := encryptedToken(t)
	items := []db.PlaidItem{
		{ID: uuid.New(), BudgetProfileID: profileA, AccessToken: token},
		{ID: uuid.New(), BudgetProfileID: profileB, AccessToken: token},
		{ID: uuid.New(), BudgetProfileID: profileA, AccessToken: token},
	}

	profiles, err := syncAllSvc(t, "pro", items, nil).SyncAll(context.Background())

	require.NoError(t, err)
	require.Len(t, profiles, 2, "one entry per budget, not per connection")
	byProfile := map[uuid.UUID]int{}
	for _, p := range profiles {
		byProfile[p.ProfileID] = len(p.Items)
	}
	assert.Equal(t, 2, byProfile[profileA])
	assert.Equal(t, 1, byProfile[profileB])
}

func TestSyncAll_ReportsUnentitledSkipInsteadOfSwallowingIt(t *testing.T) {
	profileID := uuid.New()
	inst := "Chase"
	items := []db.PlaidItem{
		{ID: uuid.New(), BudgetProfileID: profileID, InstitutionName: &inst, AccessToken: encryptedToken(t)},
	}

	profiles, err := syncAllSvc(t, "free", items, nil).SyncAll(context.Background())

	require.NoError(t, err)
	require.Len(t, profiles, 1)
	require.Len(t, profiles[0].Items, 1)
	result := profiles[0].Items[0]
	// Previously this returned a bare nil and logged one line, which is how a
	// connection sat unsynced for over two weeks without anyone noticing.
	assert.True(t, result.SkippedUnentitled, "a free-tier owner's connection must be reported, not silently skipped")
	assert.NoError(t, result.Err, "not being entitled is not a failure")
	assert.Equal(t, "Chase", result.InstitutionName, "the report must name the institution, not just the item UUID")
}

func TestSyncAll_AttributesImportsToTheAccountTheyCameFrom(t *testing.T) {
	profileID := uuid.New()
	token := encryptedToken(t)
	checkingID, cardID := uuid.New(), uuid.New()

	added := []plaidclient.Transaction{
		{PlaidID: "p1", Name: "Coffee", Amount: 4.5, Date: time.Now(), AccountID: "acct-checking"},
		{PlaidID: "p2", Name: "Books", Amount: 20, Date: time.Now(), AccountID: "acct-checking"},
		{PlaidID: "p3", Name: "Fuel", Amount: 40, Date: time.Now(), AccountID: "acct-card"},
	}

	svc := syncAllSvc(t, "pro", []db.PlaidItem{{ID: uuid.New(), BudgetProfileID: profileID, AccessToken: token}}, added)
	svc.transactions = &mockTransactionRepo{
		getPaymentMethodByPlaidAccountID: func(_ context.Context, plaidAccountID string) (db.PaymentMethod, error) {
			switch plaidAccountID {
			case "acct-checking":
				return db.PaymentMethod{ID: checkingID, Name: "Chase Checking ···1234"}, nil
			case "acct-card":
				return db.PaymentMethod{ID: cardID, Name: "Amex ···9012"}, nil
			}
			return db.PaymentMethod{}, errors.New("not found")
		},
	}

	profiles, err := svc.SyncAll(context.Background())

	require.NoError(t, err)
	require.Len(t, profiles, 1)
	assert.Equal(t, []AccountImport{
		{Account: "Chase Checking ···1234", Count: 2},
		{Account: "Amex ···9012", Count: 1},
	}, profiles[0].Items[0].ByAccount, "busiest account first, so the summary leads with where the activity was")
}

func TestSortedAccountImports_IsDeterministicOnTies(t *testing.T) {
	// Map iteration order is random; a run's notification body and its tests
	// must not be.
	for i := 0; i < 20; i++ {
		got := sortedAccountImports(map[string]int{"Zebra": 2, "Apple": 2, "Middle": 5})
		assert.Equal(t, []AccountImport{
			{Account: "Middle", Count: 5},
			{Account: "Apple", Count: 2},
			{Account: "Zebra", Count: 2},
		}, got)
	}
}

func TestListSyncWarnings_FlagsTheCallersOwnConnections(t *testing.T) {
	callerID, otherID := uuid.New(), uuid.New()
	profileID := uuid.New()

	svc := NewPlaidService(
		&mockPlaidClient{},
		&mockPlaidRepo{
			listUnsyncable: func(_ context.Context, _ uuid.UUID) ([]db.ListUnsyncableConnectionsForUserRow, error) {
				return []db.ListUnsyncableConnectionsForUserRow{
					{BudgetProfileID: profileID, BudgetName: "Household", MemberUserID: otherID, MemberName: "Alex", ConnectionCount: 2},
					{BudgetProfileID: profileID, BudgetName: "Household", MemberUserID: callerID, MemberName: "Me", ConnectionCount: 1},
				}, nil
			},
		},
		&mockBudgetProfileRepo{}, &mockUserRepo{}, &mockTransactionRepo{},
		&mockFixedExpenseRepo{}, &mockTransactionReviewRepo{}, testEncKey,
	)

	warnings, err := svc.ListSyncWarnings(context.Background(), callerID)

	require.NoError(t, err)
	require.Len(t, warnings, 2)
	assert.False(t, warnings[0].IsCurrentUser, "someone else's connections are named")
	assert.Equal(t, int32(2), warnings[0].ConnectionCount)
	assert.True(t, warnings[1].IsCurrentUser, "the caller's own get an upgrade prompt instead of a name")
}

// A transaction that posts after its period closes must not settle anything.
// Before issue #41, the import was filed into the archived period it was dated
// in, while the match target was drawn only from *live* periods newest-first —
// so a late payment reached forward and marked the following period's bill
// paid, and the review recording it landed in the archived period where
// ListTransactionReviews filtered it out. Both clients then showed the bill as
// paid with no link, and that period's real payment had nothing left to match.
func TestSyncItem_ArchivedPeriodImport_DoesNotMarkAnythingPaid(t *testing.T) {
	itemID := uuid.New()
	profileID := uuid.New()
	archivedPeriodID := uuid.New()
	feID := uuid.New()
	encrypted, err := crypto.Encrypt("real-access-token", testEncKey)
	require.NoError(t, err)

	var markedPaidCalled, excludedCalled, reviewCreated bool
	var unpaidLookupCalled bool

	svc := NewPlaidService(
		&mockPlaidClient{
			syncTransactions: func(_ context.Context, _, _ string) ([]plaidclient.Transaction, []plaidclient.Transaction, []string, string, error) {
				return []plaidclient.Transaction{
					{PlaidID: "plaid-tx-late", Name: "Patreon", Amount: 15.00, Date: time.Now().AddDate(0, 0, -20)},
				}, nil, nil, "new-cursor", nil
			},
		},
		&mockPlaidRepo{},
		&mockBudgetProfileRepo{
			getPeriodByDate: func(_ context.Context, _ uuid.UUID, _ pgtype.Date) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: archivedPeriodID, IsArchived: true}, nil
			},
		},
		&mockUserRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
				return db.User{Plan: "pro"}, nil
			},
		},
		&mockTransactionRepo{
			createTransactionFromPlaid: func(_ context.Context, _ db.CreateTransactionFromPlaidParams) (db.Transaction, error) {
				return db.Transaction{ID: uuid.New()}, nil
			},
			markAsPaid: func(_ context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				markedPaidCalled = true
				return db.Transaction{ID: arg.ID}, nil
			},
			setExcluded: func(_ context.Context, arg db.SetTransactionExcludedParams) (db.Transaction, error) {
				excludedCalled = true
				return db.Transaction{ID: arg.ID}, nil
			},
		},
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return []db.FixedExpense{makeFixedExpense(t, feID, "Creator Support", "15.00")}, nil
			},
			getUnpaidTransactionInPer: func(_ context.Context, _ db.GetUnpaidTransactionByFixedExpenseInPeriodParams) (db.Transaction, error) {
				unpaidLookupCalled = true
				return db.Transaction{}, apperr.NotFound("transaction", feID.String())
			},
		},
		&mockTransactionReviewRepo{
			listAliases: func(_ context.Context, _ uuid.UUID) ([]string, error) {
				return []string{"Patreon"}, nil
			},
			create: func(_ context.Context, _, transactionID, matchedTransactionID uuid.UUID, _ float64) (db.TransactionReview, error) {
				reviewCreated = true
				return db.TransactionReview{ID: uuid.New(), TransactionID: transactionID, MatchedTransactionID: matchedTransactionID}, nil
			},
		},
		testEncKey,
	)

	item := db.PlaidItem{ID: itemID, BudgetProfileID: profileID, AccessToken: encrypted}
	require.NoError(t, svc.SyncItem(context.Background(), item))

	assert.False(t, unpaidLookupCalled, "must not even look for a target when the period is archived")
	assert.False(t, markedPaidCalled, "must not mark a fixed expense paid from an archived period")
	assert.False(t, excludedCalled, "must not exclude an import in an archived period")
	assert.False(t, reviewCreated, "must not record a review against an archived period")
}

// The target must live in the imported transaction's own period. A perfect
// alias+amount match against a bill in a *different* period is not a match.
func TestSyncItem_NoTargetInOwnPeriod_DoesNotReachIntoAnother(t *testing.T) {
	itemID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	otherPeriodID := uuid.New()
	feID := uuid.New()
	encrypted, err := crypto.Encrypt("real-access-token", testEncKey)
	require.NoError(t, err)

	var markedPaidCalled, reviewCreated bool
	var askedForPeriod uuid.UUID

	svc := NewPlaidService(
		&mockPlaidClient{
			syncTransactions: func(_ context.Context, _, _ string) ([]plaidclient.Transaction, []plaidclient.Transaction, []string, string, error) {
				return []plaidclient.Transaction{
					{PlaidID: "plaid-tx-1", Name: "Patreon", Amount: 15.00, Date: time.Now()},
				}, nil, nil, "new-cursor", nil
			},
		},
		&mockPlaidRepo{},
		&mockBudgetProfileRepo{
			getPeriodByDate: func(_ context.Context, _ uuid.UUID, _ pgtype.Date) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID}, nil
			},
		},
		&mockUserRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
				return db.User{Plan: "pro"}, nil
			},
		},
		&mockTransactionRepo{
			createTransactionFromPlaid: func(_ context.Context, _ db.CreateTransactionFromPlaidParams) (db.Transaction, error) {
				return db.Transaction{ID: uuid.New()}, nil
			},
			markAsPaid: func(_ context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				markedPaidCalled = true
				return db.Transaction{ID: arg.ID}, nil
			},
		},
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return []db.FixedExpense{makeFixedExpense(t, feID, "Creator Support", "15.00")}, nil
			},
			// The only unpaid bill sits in another period, so this returns nothing.
			getUnpaidTransactionInPer: func(_ context.Context, arg db.GetUnpaidTransactionByFixedExpenseInPeriodParams) (db.Transaction, error) {
				askedForPeriod = arg.BudgetPeriodID
				return db.Transaction{}, apperr.NotFound("transaction", arg.FixedExpenseID.String())
			},
		},
		&mockTransactionReviewRepo{
			listAliases: func(_ context.Context, _ uuid.UUID) ([]string, error) {
				return []string{"Patreon"}, nil
			},
			create: func(_ context.Context, _, transactionID, matchedTransactionID uuid.UUID, _ float64) (db.TransactionReview, error) {
				reviewCreated = true
				return db.TransactionReview{ID: uuid.New(), TransactionID: transactionID, MatchedTransactionID: matchedTransactionID}, nil
			},
		},
		testEncKey,
	)

	item := db.PlaidItem{ID: itemID, BudgetProfileID: profileID, AccessToken: encrypted}
	require.NoError(t, svc.SyncItem(context.Background(), item))

	assert.Equal(t, periodID, askedForPeriod, "must look for the target in the import's own period")
	assert.NotEqual(t, otherPeriodID, askedForPeriod)
	assert.False(t, markedPaidCalled, "no target in this period means nothing is marked paid")
	assert.False(t, reviewCreated, "and no review is recorded")
}

// A half-applied auto-match must not be reported as a success. The bill is
// marked paid, then the review insert fails — the run summary previously
// counted this as auto-confirmed and logged success, which is why #41 could
// only be diagnosed from the database rather than the logs.
func TestSyncItem_AutoConfirmReviewFails_NotCountedAsConfirmed(t *testing.T) {
	itemID := uuid.New()
	profileID := uuid.New()
	periodID := uuid.New()
	feID := uuid.New()
	unpaidTxID := uuid.New()
	encrypted, err := crypto.Encrypt("real-access-token", testEncKey)
	require.NoError(t, err)

	var excludedCalled bool
	var notificationBody string

	notifRepo := &mockNotifRepo{
		create: func(_ context.Context, arg db.CreateNotificationParams) (db.Notification, error) {
			notificationBody = arg.Body
			return db.Notification{ID: uuid.New()}, nil
		},
	}

	svc := NewPlaidService(
		&mockPlaidClient{
			syncTransactions: func(_ context.Context, _, _ string) ([]plaidclient.Transaction, []plaidclient.Transaction, []string, string, error) {
				return []plaidclient.Transaction{
					{PlaidID: "plaid-tx-1", Name: "Patreon", Amount: 15.00, Date: time.Now()},
				}, nil, nil, "new-cursor", nil
			},
		},
		&mockPlaidRepo{},
		&mockBudgetProfileRepo{
			getPeriodByDate: func(_ context.Context, _ uuid.UUID, _ pgtype.Date) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID}, nil
			},
		},
		&mockUserRepo{
			getByID: func(_ context.Context, _ uuid.UUID) (db.User, error) {
				return db.User{Plan: "pro"}, nil
			},
		},
		&mockTransactionRepo{
			createTransactionFromPlaid: func(_ context.Context, _ db.CreateTransactionFromPlaidParams) (db.Transaction, error) {
				return db.Transaction{ID: uuid.New()}, nil
			},
			markAsPaid: func(_ context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
				return db.Transaction{ID: arg.ID}, nil
			},
			setExcluded: func(_ context.Context, arg db.SetTransactionExcludedParams) (db.Transaction, error) {
				excludedCalled = true
				return db.Transaction{ID: arg.ID}, nil
			},
		},
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return []db.FixedExpense{makeFixedExpense(t, feID, "Creator Support", "15.00")}, nil
			},
			getUnpaidTransactionInPer: func(_ context.Context, _ db.GetUnpaidTransactionByFixedExpenseInPeriodParams) (db.Transaction, error) {
				return db.Transaction{ID: unpaidTxID, BudgetPeriodID: &periodID, PlannedAmount: numericFromString(t, "15.00")}, nil
			},
		},
		&mockTransactionReviewRepo{
			listAliases: func(_ context.Context, _ uuid.UUID) ([]string, error) {
				return []string{"Patreon"}, nil
			},
			create: func(_ context.Context, _, _, _ uuid.UUID, _ float64) (db.TransactionReview, error) {
				return db.TransactionReview{}, errors.New("conflict")
			},
		},
		testEncKey,
	).WithNotifications(newTestNotifSvc(notifRepo, &mockBudgetProfileRepo{}))

	item := db.PlaidItem{ID: itemID, BudgetProfileID: profileID, AccessToken: encrypted}
	require.NoError(t, svc.SyncItem(context.Background(), item))

	assert.False(t, excludedCalled, "must stop once the review can't be recorded, rather than excluding an import with no link")
	assert.NotContains(t, notificationBody, "auto-confirmed", "a failed match must not be reported to the user as auto-confirmed")
}
