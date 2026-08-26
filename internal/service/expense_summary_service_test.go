package service

import (
	"context"
	"github.com/BeWellSpent/wellspent-backend/internal/category"
	"testing"
	"time"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"go.uber.org/zap"
)

// n is a short numericFromString wrapper for readability in table-shaped test data.
func n(t *testing.T, s string) pgtype.Numeric {
	t.Helper()
	return numericFromString(t, s)
}

// newTestExpenseSummarySvc wires an ExpenseSummaryService with the given
// mocks; a caller can leave any mock's fields nil if that layer of data
// doesn't matter for the scenario under test — the underlying mocks already
// return zero values/empty slices when their function field is unset.
func newTestExpenseSummarySvc(profiles *mockBudgetProfileRepo, transactions *mockTransactionRepo, allocations *mockExpenseAllocationRepo, fixedExpenses *mockFixedExpenseRepo) *ExpenseSummaryService {
	if profiles == nil {
		profiles = &mockBudgetProfileRepo{}
	}
	if transactions == nil {
		transactions = &mockTransactionRepo{}
	}
	if allocations == nil {
		allocations = &mockExpenseAllocationRepo{}
	}
	if fixedExpenses == nil {
		fixedExpenses = &mockFixedExpenseRepo{}
	}
	return NewExpenseSummaryService(profiles, transactions, allocations, fixedExpenses, zap.NewNop())
}

func TestGetSummary_ForbiddenWhenNotMember(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	ownerID := uuid.New()
	callerID := uuid.New()

	profiles := &mockBudgetProfileRepo{
		getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
			return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
		},
		getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
			return db.BudgetProfile{ID: profileID, UserID: ownerID}, nil
		},
		getPersonByUserID: func(_ context.Context, _, _ uuid.UUID) (db.BudgetToProfileMapping, error) {
			return db.BudgetToProfileMapping{}, apperr.NotFound("person", "not found")
		},
	}
	svc := newTestExpenseSummarySvc(profiles, nil, nil, nil)

	_, err := svc.GetSummary(context.Background(), periodID, callerID)

	require.Error(t, err)
	var forbiddenErr *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbiddenErr)
}

// TestGetSummary_PlannedButUnspentCategory_VisibleInOverview is the direct
// regression test for issue #35: a category with a plan but zero actual
// spend this period must still appear in overview_categories and count
// toward total_planned/remainder_planned — this is exactly the case iOS's
// old client-side actual-only filter silently dropped.
func TestGetSummary_PlannedButUnspentCategory_VisibleInOverview(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	catID := int32(10)

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			listIncomeSources: func(_ context.Context, _ uuid.UUID) ([]db.IncomeSource, error) {
				return []db.IncomeSource{{DefaultAmount: n(t, "1000.00")}}, nil
			},
			listIncomeEntries: func(_ context.Context, _ uuid.UUID) ([]db.IncomeEntry, error) {
				return []db.IncomeEntry{{Amount: n(t, "1000.00")}}, nil
			},
		},
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				return nil, nil // no transactions at all this period
			},
		},
		&mockExpenseAllocationRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.ExpenseAllocation, error) {
				return []db.ExpenseAllocation{{CategoryID: catID, PlannedAmount: n(t, "50.00")}}, nil
			},
		},
		nil,
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	require.Len(t, resp.OverviewCategories, 1, "planned-but-unspent category must still appear in Overview")
	cat := resp.OverviewCategories[0]
	assert.Equal(t, catID, cat.CategoryId)
	assert.Equal(t, int64(50), cat.PlannedTotal.Units)
	assert.Equal(t, int64(0), cat.ActualTotal.Units)
	assert.False(t, cat.IsOver)

	assert.Equal(t, int64(50), resp.TotalPlanned.Units)
	assert.Equal(t, int64(0), resp.TotalActual.Units)
	assert.Equal(t, int64(950), resp.RemainderPlanned.Units, "remainder_planned must subtract the unspent plan, matching the Plan tab")
	assert.Equal(t, int64(1000), resp.RemainderActual.Units)
}

func TestGetSummary_BasicPlanAndOverviewParity(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	catID := int32(1)
	pmID := uuid.New()

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			listIncomeSources: func(_ context.Context, _ uuid.UUID) ([]db.IncomeSource, error) {
				return []db.IncomeSource{{DefaultAmount: n(t, "500.00")}}, nil
			},
			listIncomeEntries: func(_ context.Context, _ uuid.UUID) ([]db.IncomeEntry, error) {
				return []db.IncomeEntry{{Amount: n(t, "500.00")}}, nil
			},
		},
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				variableType := int32(2)
				return []db.Transaction{
					{
						CategoryID:        &catID,
						PaymentMethodID:   &pmID,
						Amount:            n(t, "60.00"),
						PlannedAmount:     n(t, "60.00"),
						TransactionTypeID: &variableType,
						IsPaid:            true,
					},
				}, nil
			},
		},
		&mockExpenseAllocationRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.ExpenseAllocation, error) {
				return []db.ExpenseAllocation{{CategoryID: catID, PlannedAmount: n(t, "100.00")}}, nil
			},
		},
		nil,
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	assert.Equal(t, int64(100), resp.TotalCommitted.Units)
	assert.Equal(t, int64(100), resp.TotalPlanned.Units)
	assert.Equal(t, int64(60), resp.TotalActual.Units)
	assert.Equal(t, int64(400), resp.RemainderPlan.Units)
	assert.Equal(t, int64(440), resp.RemainderActual.Units)
	assert.Equal(t, int64(400), resp.RemainderPlanned.Units)
	assert.Equal(t, int64(0), resp.TotalOverBudget.Units)
	assert.Equal(t, int64(0), resp.TotalUnplanned.Units)

	require.Len(t, resp.PlanCategories, 1)
	require.Len(t, resp.OverviewCategories, 1)
	assert.Equal(t, int64(100), resp.PlanCategories[0].PlannedTotal.Units)
	assert.Equal(t, int64(100), resp.OverviewCategories[0].PlannedTotal.Units)
	assert.Equal(t, int64(60), resp.OverviewCategories[0].ActualTotal.Units)
}

func TestGetSummary_FixedFallback_DueThisPeriod_CountsTowardCommitted(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	catID := int32(2)
	fixedType := int32(1)

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				return []db.Transaction{
					{
						CategoryID:        &catID,
						Amount:            n(t, "30.00"),
						PlannedAmount:     n(t, "30.00"),
						TransactionTypeID: &fixedType,
						IsPaid:            false, // unpaid: shouldn't count as actual, should still count as planned
					},
				}, nil
			},
		},
		nil, // no allocation for this category — must fall back to the Fixed transaction's planned amount
		nil,
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	require.Len(t, resp.PlanCategories, 1)
	assert.Equal(t, int64(30), resp.PlanCategories[0].PlannedTotal.Units)
	assert.Equal(t, int64(30), resp.TotalCommitted.Units, "a Fixed obligation due this period counts toward committed even with no allocation")

	require.Len(t, resp.OverviewCategories, 1)
	assert.Equal(t, int64(0), resp.OverviewCategories[0].ActualTotal.Units, "unpaid Fixed transaction must not count as actual spend")
}

// TestGetSummary_NotDueFixedTemplate_InformationalOnly pins issue #48: an
// active Fixed expense template not yet spawned as a transaction this period
// is an obligation this period doesn't owe. It stays visible on the Plan tab
// so the category doesn't vanish between due periods, but its amount travels
// in not_due_planned_total and counts toward nothing.
//
// This replaces an earlier test that asserted the opposite — that the amount
// fed planned_total on both tabs and Overview's total_planned but not the
// Plan tab's total_committed. That asymmetry was inherited from web's
// pre-RPC implementation and is what the issue reported.
func TestGetSummary_NotDueFixedTemplate_InformationalOnly(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	catID := int32(3)

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				return nil, nil // no transaction spawned this period
			},
		},
		nil,
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return []db.FixedExpense{{CategoryID: &catID, PlannedAmount: n(t, "20.00"), IsActive: true}}, nil
			},
		},
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	require.Len(t, resp.PlanCategories, 1, "not-due Fixed expense category must still be visible on the Plan tab")
	row := resp.PlanCategories[0]
	assert.Equal(t, int64(0), row.PlannedTotal.Units, "a bill that isn't due yet is not planned spending for this period")
	require.NotNil(t, row.NotDuePlannedTotal, "the amount must still reach the client as informational")
	assert.Equal(t, int64(20), row.NotDuePlannedTotal.Units)
	assert.NotNil(t, row.NextDueDate, "the caption needs a date to be worth showing")

	assert.Equal(t, int64(0), resp.TotalCommitted.Units, "not-yet-due Fixed obligation must not count toward committed/remainder")
	assert.Equal(t, int64(0), resp.TotalPlanned.Units, "nor toward the Overview's planned total")
	assert.Equal(t, int64(0), resp.RemainderPlanned.Units)

	assert.Empty(t, resp.OverviewCategories, "with no actual spend and no plan, there is nothing to show on the Overview tab")
}

// TestGetSummary_NotDueFixedTemplate_EarliestDueDateWins covers a category
// with several upcoming templates: the caption answers "when does this
// category next cost me anything", so the nearest date is the useful one.
func TestGetSummary_NotDueFixedTemplate_EarliestDueDateWins(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	catID := int32(3)

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				return nil, nil
			},
		},
		nil,
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return []db.FixedExpense{
					{CategoryID: &catID, PlannedAmount: n(t, "20.00"), IsActive: true, DayOfMonth: 28},
					{CategoryID: &catID, PlannedAmount: n(t, "5.00"), IsActive: true, DayOfMonth: 2},
				}, nil
			},
		},
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	require.Len(t, resp.PlanCategories, 1)
	row := resp.PlanCategories[0]
	require.NotNil(t, row.NotDuePlannedTotal)
	assert.Equal(t, int64(25), row.NotDuePlannedTotal.Units, "both upcoming templates in the category are summed")

	require.NotNil(t, row.NextDueDate)
	earliest := FixedExpenseNextDueDate(db.FixedExpense{DayOfMonth: 2}, time.Now().UTC())
	assert.Equal(t, earliest.Unix(), row.NextDueDate.AsTime().Unix(), "the nearer of the two due dates wins")
}

// TestGetSummary_PlanRowsSumToTotalCommitted is the invariant issue #48 broke:
// a user reading the Plan tab adds up the rows and expects the total. With
// the not-due tier feeding rows but not the total, they didn't match.
func TestGetSummary_PlanRowsSumToTotalCommitted(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	allocCat := int32(1)
	dueCat := int32(2)
	notDueCat := int32(3)
	methodID := uuid.New()

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				txType := fixedTransactionTypeID
				return []db.Transaction{{
					CategoryID: &dueCat, PaymentMethodID: &methodID,
					TransactionTypeID: &txType,
					PlannedAmount:     n(t, "30.00"), Amount: n(t, "0.00"),
				}}, nil
			},
		},
		&mockExpenseAllocationRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.ExpenseAllocation, error) {
				return []db.ExpenseAllocation{{CategoryID: allocCat, PlannedAmount: n(t, "100.00")}}, nil
			},
		},
		&mockFixedExpenseRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.FixedExpense, error) {
				return []db.FixedExpense{{CategoryID: &notDueCat, PlannedAmount: n(t, "20.00"), IsActive: true}}, nil
			},
		},
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	require.Len(t, resp.PlanCategories, 3, "the not-due category is still listed, just at zero")
	var sum int64
	for _, row := range resp.PlanCategories {
		sum += row.PlannedTotal.Units
	}
	assert.Equal(t, int64(130), sum)
	assert.Equal(t, sum, resp.TotalCommitted.Units, "the rows must add up to the total shown beneath them")
}

func TestGetSummary_SavingsCategory_UsesSavingsSourceSum(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	savingsCatID := int32(4)

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			listSavingsSources: func(_ context.Context, _ uuid.UUID) ([]db.SavingsSource, error) {
				return []db.SavingsSource{{Amount: n(t, "200.00")}, {Amount: n(t, "50.00")}}, nil
			},
		},
		&mockTransactionRepo{
			listSystemCategories: func(_ context.Context) (map[category.Key]int32, error) {
				return map[category.Key]int32{category.Savings: savingsCatID}, nil
			},
		},
		// Deliberately also give the Savings category an allocation — it must
		// be ignored, since Savings is system-managed from savings sources.
		&mockExpenseAllocationRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.ExpenseAllocation, error) {
				return []db.ExpenseAllocation{{CategoryID: savingsCatID, PlannedAmount: n(t, "999.00")}}, nil
			},
		},
		nil,
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	require.Len(t, resp.PlanCategories, 1)
	assert.Equal(t, int64(250), resp.PlanCategories[0].PlannedTotal.Units, "Savings planned total is the sum of savings sources, not the allocation")
	assert.Equal(t, int64(250), resp.TotalCommitted.Units)
}

func TestGetSummary_ExclusionRules_IsExcludedAndIncomeCategory(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	catID := int32(5)
	incomeCatID := int32(6)
	variableType := int32(2)

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{
			listSystemCategories: func(_ context.Context) (map[category.Key]int32, error) {
				return map[category.Key]int32{category.Income: incomeCatID}, nil
			},
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				return []db.Transaction{
					{CategoryID: &catID, Amount: n(t, "40.00"), PlannedAmount: n(t, "40.00"), TransactionTypeID: &variableType, IsExcluded: true},
					{CategoryID: &incomeCatID, Amount: n(t, "2000.00"), PlannedAmount: n(t, "2000.00"), TransactionTypeID: &variableType},
					{CategoryID: &catID, Amount: n(t, "15.00"), PlannedAmount: n(t, "15.00"), TransactionTypeID: &variableType},
				}, nil
			},
		},
		nil,
		nil,
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	require.Len(t, resp.OverviewCategories, 1, "only the one non-excluded, non-Income transaction's category should be visible")
	assert.Equal(t, catID, resp.OverviewCategories[0].CategoryId)
	assert.Equal(t, int64(15), resp.OverviewCategories[0].ActualTotal.Units, "the manually-excluded $40 transaction must not count")
	assert.Equal(t, int64(15), resp.TotalActual.Units, "the $2000 Income-category transaction must not count as spend")
}

// A credit card payment arrives TWICE from Plaid: positive on the account that
// paid and negative on the card that was paid, both filed under Payment. While
// nothing filtered that category, the negative leg cancelled the card's own
// purchases — a Costco Visa carrying $1,477.86 of real spend reported −$29.01,
// and the checking account that settled it absorbed the difference.
//
// Real production figures, kept verbatim so the case stays recognisable.
func TestGetSummary_CardPaymentDoesNotCancelTheCardsOwnPurchases(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	cardID := uuid.New()
	personID := int32(1)
	shoppingCatID := int32(20)
	paymentCatID := int32(23)
	variableType := int32(2)

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			listPeople: func(_ context.Context, _ uuid.UUID) ([]db.BudgetToProfileMapping, error) {
				return []db.BudgetToProfileMapping{{ID: personID}}, nil
			},
		},
		&mockTransactionRepo{
			listSystemCategories: func(_ context.Context) (map[category.Key]int32, error) {
				return map[category.Key]int32{category.Payment: paymentCatID}, nil
			},
			listPaymentMethods: func(_ context.Context, _ uuid.UUID) ([]db.ListPaymentMethodsRow, error) {
				p := personID
				return []db.ListPaymentMethodsRow{{ID: cardID, BudgetPersonID: &p}}, nil
			},
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				pm := cardID
				return []db.Transaction{
					{CategoryID: &shoppingCatID, PaymentMethodID: &pm, Amount: n(t, "1477.86"), PlannedAmount: n(t, "1477.86"), TransactionTypeID: &variableType},
					// The two "ONLINE PAYMENT, THANK YOU" rows on the card.
					{CategoryID: &paymentCatID, PaymentMethodID: &pm, Amount: n(t, "-988.35"), PlannedAmount: n(t, "-988.35"), TransactionTypeID: &variableType},
					{CategoryID: &paymentCatID, PaymentMethodID: &pm, Amount: n(t, "-518.52"), PlannedAmount: n(t, "-518.52"), TransactionTypeID: &variableType},
				}, nil
			},
		},
		nil,
		nil,
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	assert.Equal(t, int64(1477), resp.TotalActual.Units, "card payments must not net against the card's purchases")
	assert.Equal(t, int32(860000000), resp.TotalActual.Nanos)
	assert.Equal(t, int64(1477), resp.VariableActualTotal.Units)

	require.Len(t, resp.OverviewCategories, 1, "the Payment category should not appear as spending at all")
	assert.Equal(t, shoppingCatID, resp.OverviewCategories[0].CategoryId)

	// The person the card is attributed to is credited with the purchases, not
	// with purchases-minus-settlements.
	breakdowns := resp.OverviewCategories[0].PersonBreakdowns
	require.Len(t, breakdowns, 1)
	assert.Equal(t, int64(1477), breakdowns[0].ActualTotal.Units)
}

func TestGetSummary_OverBudgetAndUnplanned(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	plannedCatID := int32(7)
	variableType := int32(2)

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				return []db.Transaction{
					// Over budget: planned 50, actual 80.
					{CategoryID: &plannedCatID, Amount: n(t, "80.00"), PlannedAmount: n(t, "80.00"), TransactionTypeID: &variableType, IsPaid: true},
					// Fully uncategorized spend.
					{CategoryID: nil, Amount: n(t, "25.00"), PlannedAmount: n(t, "25.00"), TransactionTypeID: &variableType, IsPaid: true},
				}, nil
			},
		},
		&mockExpenseAllocationRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.ExpenseAllocation, error) {
				return []db.ExpenseAllocation{{CategoryID: plannedCatID, PlannedAmount: n(t, "50.00")}}, nil
			},
		},
		nil,
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	require.Len(t, resp.OverviewCategories, 1)
	assert.True(t, resp.OverviewCategories[0].IsOver)
	assert.Equal(t, int64(30), resp.TotalOverBudget.Units)
	assert.Equal(t, int64(25), resp.TotalUnplanned.Units, "uncategorized spend counts toward unplanned")
	assert.Equal(t, int64(25), resp.UncategorizedActual.Units)
	assert.Equal(t, int64(105), resp.TotalActual.Units)
}

func TestGetSummary_PersonBreakdowns(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	catID := int32(8)
	person1 := int32(1)
	person2 := int32(2)

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
			listPeople: func(_ context.Context, _ uuid.UUID) ([]db.BudgetToProfileMapping, error) {
				return []db.BudgetToProfileMapping{{ID: person1}, {ID: person2}}, nil
			},
		},
		&mockTransactionRepo{},
		&mockExpenseAllocationRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.ExpenseAllocation, error) {
				p1, p2 := person1, person2
				return []db.ExpenseAllocation{
					{CategoryID: catID, BudgetPersonID: &p1, PlannedAmount: n(t, "30.00")},
					{CategoryID: catID, BudgetPersonID: &p2, PlannedAmount: n(t, "70.00")},
				}, nil
			},
		},
		nil,
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	require.Len(t, resp.PlanCategories, 1)
	breakdowns := resp.PlanCategories[0].PersonBreakdowns
	require.Len(t, breakdowns, 2)

	byPerson := map[int64]int64{}
	for _, b := range breakdowns {
		byPerson[b.BudgetPersonId] = b.PlannedTotal.Units
	}
	assert.Equal(t, int64(30), byPerson[int64(person1)])
	assert.Equal(t, int64(70), byPerson[int64(person2)])
}

// day is a short pgtype.Date builder for chronological test data.
func day(y int, m time.Month, d int) pgtype.Date {
	return pgtype.Date{Time: time.Date(y, m, d, 0, 0, 0, 0, time.UTC), Valid: true}
}

func TestGetSummary_ActualSplitByType_SumsToTotalActual(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	catID := int32(7)
	fixedType := int32(1)
	variableType := int32(2)

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				return []db.Transaction{
					{ID: uuid.New(), CategoryID: &catID, Amount: n(t, "100.00"), TransactionTypeID: &fixedType, IsPaid: true},
					// Unpaid Fixed: not spend yet, so it counts toward neither half.
					{ID: uuid.New(), CategoryID: &catID, Amount: n(t, "500.00"), TransactionTypeID: &fixedType, IsPaid: false},
					{ID: uuid.New(), CategoryID: &catID, Amount: n(t, "30.00"), TransactionTypeID: &variableType},
					// Uncategorized still has to land in a half, or the two
					// stop summing to total_actual.
					{ID: uuid.New(), CategoryID: nil, Amount: n(t, "5.00"), TransactionTypeID: &variableType},
				}, nil
			},
		},
		nil, nil,
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	assert.Equal(t, int64(100), resp.FixedActualTotal.Units, "only the paid fixed transaction counts")
	assert.Equal(t, int64(35), resp.VariableActualTotal.Units, "includes the uncategorized row")
	assert.Equal(t, int64(135), resp.TotalActual.Units)
	assert.Equal(t,
		resp.TotalActual.Units,
		resp.FixedActualTotal.Units+resp.VariableActualTotal.Units,
		"the two halves must always reconstruct total_actual")
}

func TestGetSummary_OverBudgetIDs_FlagFromCrossingPointOnward(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	catID := int32(7)
	variableType := int32(2)

	under := uuid.New()    // running 40, plan 100 — under
	crossing := uuid.New() // running 110 — this one crosses
	after := uuid.New()    // running 130 — already over

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				// Deliberately out of chronological order: the walk has to sort.
				return []db.Transaction{
					{ID: after, CategoryID: &catID, Amount: n(t, "20.00"), TransactionTypeID: &variableType, Date: day(2026, time.June, 20)},
					{ID: under, CategoryID: &catID, Amount: n(t, "40.00"), TransactionTypeID: &variableType, Date: day(2026, time.June, 1)},
					{ID: crossing, CategoryID: &catID, Amount: n(t, "70.00"), TransactionTypeID: &variableType, Date: day(2026, time.June, 10)},
				}, nil
			},
		},
		&mockExpenseAllocationRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.ExpenseAllocation, error) {
				return []db.ExpenseAllocation{{CategoryID: catID, PlannedAmount: n(t, "100.00")}}, nil
			},
		},
		nil,
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	assert.ElementsMatch(t, []string{crossing.String(), after.String()}, resp.OverBudgetTransactionIds,
		"only from the crossing point onward, not every transaction in an over-budget category")
}

func TestGetSummary_OverBudgetIDs_IgnoreFixedAndReceived(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	catID := int32(7)
	fixedType := int32(1)
	variableType := int32(2)

	received := uuid.New()
	fixed := uuid.New()

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				return []db.Transaction{
					// Paid Fixed well past the plan — never a filter candidate,
					// the filter is about variable spending.
					{ID: fixed, CategoryID: &catID, Amount: n(t, "900.00"), TransactionTypeID: &fixedType, IsPaid: true, Date: day(2026, time.June, 1)},
					// Money in cannot push a category over.
					{ID: received, CategoryID: &catID, Amount: n(t, "-50.00"), TransactionTypeID: &variableType, Date: day(2026, time.June, 2)},
				}, nil
			},
		},
		&mockExpenseAllocationRepo{
			list: func(_ context.Context, _ uuid.UUID) ([]db.ExpenseAllocation, error) {
				return []db.ExpenseAllocation{{CategoryID: catID, PlannedAmount: n(t, "10.00")}}, nil
			},
		},
		nil,
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	assert.Empty(t, resp.OverBudgetTransactionIds)
}

func TestGetSummary_OverBudgetIDs_UnplannedCategoryFlagsFromFirstSpend(t *testing.T) {
	profileID := uuid.New()
	periodID := uuid.New()
	userID := uuid.New()
	catID := int32(7)
	variableType := int32(2)
	first := uuid.New()

	svc := newTestExpenseSummarySvc(
		&mockBudgetProfileRepo{
			getPeriodByID: func(_ context.Context, _ uuid.UUID) (db.BudgetPeriod, error) {
				return db.BudgetPeriod{ID: periodID, BudgetProfileID: profileID}, nil
			},
			getByID: func(_ context.Context, _ uuid.UUID) (db.BudgetProfile, error) {
				return db.BudgetProfile{ID: profileID, UserID: userID}, nil
			},
		},
		&mockTransactionRepo{
			list: func(_ context.Context, _ db.ListTransactionsParams) ([]db.Transaction, error) {
				return []db.Transaction{
					{ID: first, CategoryID: &catID, Amount: n(t, "5.00"), TransactionTypeID: &variableType, Date: day(2026, time.June, 1)},
				}, nil
			},
		},
		nil, nil,
	)

	resp, err := svc.GetSummary(context.Background(), periodID, userID)
	require.NoError(t, err)

	assert.Equal(t, []string{first.String()}, resp.OverBudgetTransactionIds,
		"spending in a category with no plan is over budget from its first transaction")
}
