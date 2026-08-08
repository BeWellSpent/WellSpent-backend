package service

import (
	"context"
	"testing"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
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
	return NewExpenseSummaryService(profiles, transactions, allocations, fixedExpenses)
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

// TestGetSummary_NotDueFixedTemplate_VisibleButExcludedFromCommitted covers
// the asymmetry that exists in the current (correct) web reference: an
// active Fixed expense template not yet spawned as a transaction this
// period shows up as a planned amount on both tabs, but only Overview's
// total_planned counts it — the Plan tab's total_committed deliberately
// excludes it since the obligation isn't due against this period's income
// yet.
func TestGetSummary_NotDueFixedTemplate_VisibleButExcludedFromCommitted(t *testing.T) {
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
	assert.Equal(t, int64(20), resp.PlanCategories[0].PlannedTotal.Units)
	assert.Equal(t, int64(0), resp.TotalCommitted.Units, "not-yet-due Fixed obligation must not count toward committed/remainder")

	require.Len(t, resp.OverviewCategories, 1)
	assert.Equal(t, int64(20), resp.OverviewCategories[0].PlannedTotal.Units)
	assert.Equal(t, int64(20), resp.TotalPlanned.Units, "Overview's total_planned DOES include the not-due fallback")
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
			listSystemCategories: func(_ context.Context) (map[string]int32, error) {
				return map[string]int32{"Savings": savingsCatID}, nil
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
			listSystemCategories: func(_ context.Context) (map[string]int32, error) {
				return map[string]int32{"Income": incomeCatID}, nil
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
