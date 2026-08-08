package service

import (
	"context"
	"math/big"
	"sort"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
)

// fixedTransactionTypeID matches transaction_type (1=Fixed, 2=Variable),
// seeded in migration 000001.
const fixedTransactionTypeID int32 = 1

// ExpenseSummaryService computes, server-side, the planned/actual/remainder
// totals the Expense Plan and Expense Overview tabs need. This was
// previously reimplemented independently in WellSpent-web and
// WellSpent-iOS and drifted apart — iOS's Overview category-visibility
// filter was missing web's "actual spend OR has a plan" union, silently
// dropping planned-but-unspent categories from its Remaining total
// (see docs/features/expense-summary.md, issue #35). This service is the
// single source of truth both clients (and any future client) consume via
// GetExpenseSummary instead of re-deriving it.
type ExpenseSummaryService struct {
	profiles      repository.BudgetProfileRepository
	transactions  repository.TransactionRepository
	allocations   repository.ExpenseAllocationRepository
	fixedExpenses repository.FixedExpenseRepository
}

func NewExpenseSummaryService(
	profiles repository.BudgetProfileRepository,
	transactions repository.TransactionRepository,
	allocations repository.ExpenseAllocationRepository,
	fixedExpenses repository.FixedExpenseRepository,
) *ExpenseSummaryService {
	if profiles == nil {
		panic("NewExpenseSummaryService: profiles is required")
	}
	if transactions == nil {
		panic("NewExpenseSummaryService: transactions is required")
	}
	if allocations == nil {
		panic("NewExpenseSummaryService: allocations is required")
	}
	if fixedExpenses == nil {
		panic("NewExpenseSummaryService: fixedExpenses is required")
	}
	return &ExpenseSummaryService{
		profiles:      profiles,
		transactions:  transactions,
		allocations:   allocations,
		fixedExpenses: fixedExpenses,
	}
}

func (s *ExpenseSummaryService) assertMember(ctx context.Context, profileID, userID uuid.UUID) error {
	profile, err := s.profiles.GetByID(ctx, profileID)
	if err != nil {
		return err
	}
	if profile.UserID == userID {
		return nil
	}
	if _, err := s.profiles.GetPersonByUserID(ctx, profileID, userID); err != nil {
		return apperr.Forbidden("access denied")
	}
	return nil
}

type catPersonKey struct {
	categoryID int32
	personID   int32
}

// GetSummary computes the full expense summary for a budget period, fresh on
// every call (no caching/storage) so it reflects the current state of
// transactions/allocations/savings/fixed expenses/income exactly, including
// anything just manually added.
func (s *ExpenseSummaryService) GetSummary(ctx context.Context, periodID, userID uuid.UUID) (*v1.GetExpenseSummaryResponse, error) {
	period, err := s.profiles.GetPeriodByID(ctx, periodID)
	if err != nil {
		return nil, err
	}
	profileID := period.BudgetProfileID
	if err := s.assertMember(ctx, profileID, userID); err != nil {
		return nil, err
	}

	people, err := s.profiles.ListPeople(ctx, profileID)
	if err != nil {
		return nil, err
	}
	allocations, err := s.allocations.List(ctx, profileID)
	if err != nil {
		return nil, err
	}
	savingsSources, err := s.profiles.ListSavingsSources(ctx, profileID)
	if err != nil {
		return nil, err
	}
	allFixedExpenses, err := s.fixedExpenses.List(ctx, profileID)
	if err != nil {
		return nil, err
	}
	incomeSources, err := s.profiles.ListIncomeSources(ctx, profileID)
	if err != nil {
		return nil, err
	}
	incomeEntries, err := s.profiles.ListIncomeEntries(ctx, periodID)
	if err != nil {
		return nil, err
	}
	paymentMethods, err := s.transactions.ListPaymentMethods(ctx, profileID)
	if err != nil {
		return nil, err
	}
	systemCategories, err := s.transactions.ListSystemCategories(ctx)
	if err != nil {
		return nil, err
	}
	rawTransactions, err := s.transactions.List(ctx, db.ListTransactionsParams{BudgetPeriodID: periodID})
	if err != nil {
		return nil, err
	}

	incomeCategoryID, hasIncomeCategory := systemCategories["Income"]
	savingsCategoryID, hasSavingsCategory := systemCategories["Savings"]

	// Same exclusion rule as web's isTransactionExcluded/iOS's
	// ExpenseOverviewCalculations.isTransactionExcluded: is_excluded, or the
	// Income system category (payroll deposits aren't spend).
	transactions := make([]db.Transaction, 0, len(rawTransactions))
	for _, tx := range rawTransactions {
		if tx.IsExcluded {
			continue
		}
		if hasIncomeCategory && tx.CategoryID != nil && *tx.CategoryID == incomeCategoryID {
			continue
		}
		transactions = append(transactions, tx)
	}

	activeFixedExpenses := make([]db.FixedExpense, 0, len(allFixedExpenses))
	for _, fe := range allFixedExpenses {
		if fe.IsActive {
			activeFixedExpenses = append(activeFixedExpenses, fe)
		}
	}

	pmPersonMap := make(map[uuid.UUID]int32, len(paymentMethods))
	for _, pm := range paymentMethods {
		if pm.BudgetPersonID != nil {
			pmPersonMap[pm.ID] = *pm.BudgetPersonID
		}
	}

	// ─── Actual totals — unpaid Fixed transactions don't count as spent yet ───
	actualByCat := map[int32]int64{}
	actualByPersonCat := map[catPersonKey]int64{}
	var uncategorizedActual int64
	for _, tx := range transactions {
		if tx.TransactionTypeID != nil && *tx.TransactionTypeID == fixedTransactionTypeID && !tx.IsPaid {
			continue
		}
		amt := numericToNanos(tx.Amount)
		if tx.CategoryID == nil {
			uncategorizedActual += amt
			continue
		}
		actualByCat[*tx.CategoryID] += amt
		if tx.PaymentMethodID != nil {
			if personID, ok := pmPersonMap[*tx.PaymentMethodID]; ok {
				actualByPersonCat[catPersonKey{*tx.CategoryID, personID}] += amt
			}
		}
	}

	// ─── Planned totals ───
	// Tier 1: allocations, per category and per person.
	allocByCat := map[int32]int64{}
	allocByPersonCat := map[catPersonKey]int64{}
	catIDsWithAlloc := map[int32]bool{}
	for _, a := range allocations {
		amt := numericToNanos(a.PlannedAmount)
		allocByCat[a.CategoryID] += amt
		catIDsWithAlloc[a.CategoryID] = true
		if a.BudgetPersonID != nil {
			allocByPersonCat[catPersonKey{a.CategoryID, *a.BudgetPersonID}] += amt
		}
	}

	// Tier 2: Fixed transactions due (spawned) this period.
	fixedDueByCat := map[int32]int64{}
	fixedDueByPersonCat := map[catPersonKey]int64{}
	for _, tx := range transactions {
		if tx.TransactionTypeID == nil || *tx.TransactionTypeID != fixedTransactionTypeID || tx.CategoryID == nil {
			continue
		}
		amt := numericToNanos(tx.PlannedAmount)
		fixedDueByCat[*tx.CategoryID] += amt
		if tx.PaymentMethodID != nil {
			if personID, ok := pmPersonMap[*tx.PaymentMethodID]; ok {
				fixedDueByPersonCat[catPersonKey{*tx.CategoryID, personID}] += amt
			}
		}
	}

	// Tier 3: active Fixed expense templates not yet due this period —
	// display/Overview-only fallback, deliberately excluded from the Plan
	// tab's committed/remainder math below (an obligation that isn't due
	// yet isn't "committed" against this period's income).
	notDueFixedByCat := map[int32]int64{}
	fixedExpenseCatIDs := map[int32]bool{}
	for _, fe := range activeFixedExpenses {
		if fe.CategoryID == nil {
			continue
		}
		fixedExpenseCatIDs[*fe.CategoryID] = true
		if _, ok := fixedDueByCat[*fe.CategoryID]; ok {
			continue
		}
		notDueFixedByCat[*fe.CategoryID] += numericToNanos(fe.PlannedAmount)
	}

	// Savings: system-managed category, amount is the sum of savings source
	// amounts regardless of allocations/fixed expenses.
	var savingsTotal int64
	savingsByPerson := map[int32]int64{}
	for _, ss := range savingsSources {
		amt := numericToNanos(ss.Amount)
		savingsTotal += amt
		if ss.BudgetPersonID != nil {
			savingsByPerson[*ss.BudgetPersonID] += amt
		}
	}

	isSavingsCategory := func(catID int32) bool {
		return hasSavingsCategory && catID == savingsCategoryID
	}

	// plannedTotal mirrors web's getCatPlanned/getCategoryPlanned (identical
	// on both the Plan and Overview tabs): savings first, then allocations,
	// then Fixed fallback (due this period, else not-yet-due template).
	plannedTotal := func(catID int32) int64 {
		if isSavingsCategory(catID) {
			return savingsTotal
		}
		if alloc, ok := allocByCat[catID]; ok && alloc != 0 {
			return alloc
		}
		if due, ok := fixedDueByCat[catID]; ok && due != 0 {
			return due
		}
		return notDueFixedByCat[catID]
	}

	plannedTotalForPerson := func(catID, personID int32) int64 {
		if isSavingsCategory(catID) {
			return savingsByPerson[personID]
		}
		if alloc, ok := allocByPersonCat[catPersonKey{catID, personID}]; ok && alloc != 0 {
			return alloc
		}
		return fixedDueByPersonCat[catPersonKey{catID, personID}]
	}

	// ─── Category IDs to consider — union across every source ───
	categoryIDSet := map[int32]bool{}
	for id := range actualByCat {
		categoryIDSet[id] = true
	}
	for id := range allocByCat {
		categoryIDSet[id] = true
	}
	for id := range fixedDueByCat {
		categoryIDSet[id] = true
	}
	for id := range notDueFixedByCat {
		categoryIDSet[id] = true
	}
	if hasSavingsCategory && len(savingsSources) > 0 {
		categoryIDSet[savingsCategoryID] = true
	}

	buildPersonBreakdowns := func(catID int32) []*v1.PersonExpenseSummary {
		var out []*v1.PersonExpenseSummary
		for _, p := range people {
			planned := plannedTotalForPerson(catID, p.ID)
			actual := actualByPersonCat[catPersonKey{catID, p.ID}]
			if planned == 0 && actual == 0 {
				continue
			}
			out = append(out, &v1.PersonExpenseSummary{
				BudgetPersonId: int64(p.ID),
				PlannedTotal:   nanosToMoney(planned),
				ActualTotal:    nanosToMoney(actual),
			})
		}
		return out
	}

	// ─── Plan tab: visible if it has an allocation, a Fixed obligation (due
	// or not), or is Savings with sources. Sorted by planned descending. ───
	var planCategories []*v1.CategoryExpenseSummary
	var totalCommitted int64
	for catID := range categoryIDSet {
		hasSavingsSources := isSavingsCategory(catID) && len(savingsSources) > 0
		visible := catIDsWithAlloc[catID] || hasSavingsSources || fixedDueByCat[catID] != 0 || fixedExpenseCatIDs[catID]
		if !visible {
			continue
		}
		planned := plannedTotal(catID)
		planCategories = append(planCategories, &v1.CategoryExpenseSummary{
			CategoryId:       catID,
			PlannedTotal:     nanosToMoney(planned),
			PersonBreakdowns: buildPersonBreakdowns(catID),
		})

		// Committed total intentionally excludes the not-due Fixed fallback
		// (tier 3) — matches WellSpent-web's ExpensesPanel.tsx totalCommitted.
		if isSavingsCategory(catID) {
			totalCommitted += savingsTotal
		} else if alloc, ok := allocByCat[catID]; ok && alloc != 0 {
			totalCommitted += alloc
		} else {
			totalCommitted += fixedDueByCat[catID]
		}
	}
	sort.Slice(planCategories, func(i, j int) bool {
		return planCategories[i].PlannedTotal.Units > planCategories[j].PlannedTotal.Units ||
			(planCategories[i].PlannedTotal.Units == planCategories[j].PlannedTotal.Units && planCategories[i].PlannedTotal.Nanos > planCategories[j].PlannedTotal.Nanos)
	})

	// ─── Overview tab: visible if it has actual spend OR any plan (so
	// unspent budget is visible too). Sorted by actual descending. ───
	var overviewCategories []*v1.CategoryExpenseSummary
	var totalPlanned, totalActual, totalOverBudget, totalUnplanned int64
	totalActual = uncategorizedActual
	totalUnplanned = uncategorizedActual
	for catID := range categoryIDSet {
		hasSavingsSources := isSavingsCategory(catID) && len(savingsSources) > 0
		visible := actualByCat[catID] != 0 || catIDsWithAlloc[catID] || hasSavingsSources || fixedDueByCat[catID] != 0 || notDueFixedByCat[catID] != 0
		if !visible {
			continue
		}
		planned := plannedTotal(catID)
		actual := actualByCat[catID]
		isOver := planned > 0 && actual > planned
		overviewCategories = append(overviewCategories, &v1.CategoryExpenseSummary{
			CategoryId:       catID,
			PlannedTotal:     nanosToMoney(planned),
			ActualTotal:      nanosToMoney(actual),
			IsOver:           isOver,
			PersonBreakdowns: buildPersonBreakdowns(catID),
		})
		totalPlanned += planned
		totalActual += actual
		if planned <= 0 {
			totalUnplanned += actual
		} else if actual > planned {
			totalOverBudget += actual - planned
		}
	}
	sort.Slice(overviewCategories, func(i, j int) bool {
		return overviewCategories[i].ActualTotal.Units > overviewCategories[j].ActualTotal.Units ||
			(overviewCategories[i].ActualTotal.Units == overviewCategories[j].ActualTotal.Units && overviewCategories[i].ActualTotal.Nanos > overviewCategories[j].ActualTotal.Nanos)
	})

	var incomeFromSources int64
	for _, is := range incomeSources {
		incomeFromSources += numericToNanos(is.DefaultAmount)
	}
	var incomeFromEntries int64
	for _, ie := range incomeEntries {
		incomeFromEntries += numericToNanos(ie.Amount)
	}

	return &v1.GetExpenseSummaryResponse{
		IncomeFromSources:  nanosToMoney(incomeFromSources),
		IncomeFromEntries:  nanosToMoney(incomeFromEntries),
		TotalCommitted:     nanosToMoney(totalCommitted),
		TotalPlanned:       nanosToMoney(totalPlanned),
		TotalActual:        nanosToMoney(totalActual),
		UncategorizedActual: nanosToMoney(uncategorizedActual),
		TotalOverBudget:    nanosToMoney(totalOverBudget),
		TotalUnplanned:     nanosToMoney(totalUnplanned),
		RemainderPlan:      nanosToMoney(incomeFromSources - totalCommitted),
		RemainderActual:    nanosToMoney(incomeFromEntries - totalActual),
		RemainderPlanned:   nanosToMoney(incomeFromEntries - totalPlanned),
		PlanCategories:     planCategories,
		OverviewCategories: overviewCategories,
	}, nil
}

// numericToNanos converts a pgtype.Numeric to an exact nanos-scaled int64,
// using big.Int arithmetic throughout — never a float64 intermediate. A
// float64 conversion can round a value like 19.99 to a nanos figure off by
// one, which previously broke an exact-equality check elsewhere in this
// codebase (see internal/handler/convert.go's moneyFromNumeric, fixed for
// the same reason). Every realistic budget amount fits comfortably in an
// int64 nanos count (~9.2 billion currency units), so plain int64 addition
// is used for aggregation instead of repeated big.Int allocation.
func numericToNanos(n pgtype.Numeric) int64 {
	if !n.Valid || n.Int == nil {
		return 0
	}
	nanoExp := n.Exp + 9
	total := new(big.Int).Set(n.Int)
	if nanoExp > 0 {
		scale := new(big.Int).Exp(big.NewInt(10), big.NewInt(int64(nanoExp)), nil)
		total.Mul(total, scale)
	} else if nanoExp < 0 {
		scale := new(big.Int).Exp(big.NewInt(10), big.NewInt(int64(-nanoExp)), nil)
		total.Quo(total, scale)
	}
	return total.Int64()
}

// nanosToMoney splits a nanos-scaled int64 back into proto Money's
// units + nanos, matching the sign of the original value (nanos is always
// the fractional remainder in the same direction as units, per proto Money's
// own convention).
func nanosToMoney(totalNanos int64) *v1.Money {
	units := totalNanos / 1_000_000_000
	nanos := totalNanos % 1_000_000_000
	return &v1.Money{Units: units, Nanos: int32(nanos)}
}
