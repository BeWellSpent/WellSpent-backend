package service

import (
	"context"
	"math/big"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"go.uber.org/zap"
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
	log           *zap.Logger
}

func NewExpenseSummaryService(
	profiles repository.BudgetProfileRepository,
	transactions repository.TransactionRepository,
	allocations repository.ExpenseAllocationRepository,
	fixedExpenses repository.FixedExpenseRepository,
	log *zap.Logger,
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
	if log == nil {
		panic("NewExpenseSummaryService: log is required")
	}
	return &ExpenseSummaryService{
		profiles:      profiles,
		transactions:  transactions,
		allocations:   allocations,
		fixedExpenses: fixedExpenses,
		log:           log,
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
// anything just manually added. It's a thin orchestrator: fetch every input
// (each via its own logged loadX method, so a failure names exactly which
// dependency broke) into an expenseSummaryData, then hand that to
// newExpenseSummaryCalculator to do the actual math.
func (s *ExpenseSummaryService) GetSummary(ctx context.Context, periodID, userID uuid.UUID) (*v1.GetExpenseSummaryResponse, error) {
	period, err := s.loadPeriod(ctx, periodID)
	if err != nil {
		return nil, err
	}
	profileID := period.BudgetProfileID
	if err := s.assertMember(ctx, profileID, userID); err != nil {
		return nil, err
	}

	data, err := s.loadData(ctx, period)
	if err != nil {
		return nil, err
	}

	return newExpenseSummaryCalculator(data).response(), nil
}

// ─── Data loading — one function per source, each logging its own failure ───

// expenseSummaryData holds every input GetSummary's calculation needs, all
// fetched up front so the calculator itself does no I/O.
type expenseSummaryData struct {
	period              db.BudgetPeriod
	people              []db.BudgetToProfileMapping
	allocations         []db.ExpenseAllocation
	savingsSources      []db.SavingsSource
	activeFixedExpenses []db.FixedExpense
	incomeSources       []db.IncomeSource
	incomeEntries       []db.IncomeEntry
	paymentMethods      []db.ListPaymentMethodsRow
	// transactions is already exclusion-filtered (is_excluded, Income
	// category) — every downstream computation can assume every transaction
	// here is real spending activity.
	transactions       []db.Transaction
	incomeCategoryID   int32
	hasIncomeCategory  bool
	savingsCategoryID  int32
	hasSavingsCategory bool
}

func (s *ExpenseSummaryService) loadData(ctx context.Context, period db.BudgetPeriod) (*expenseSummaryData, error) {
	profileID := period.BudgetProfileID

	incomeCategoryID, hasIncomeCategory, savingsCategoryID, hasSavingsCategory, err := s.loadSystemCategoryIDs(ctx)
	if err != nil {
		return nil, err
	}
	people, err := s.loadPeople(ctx, profileID)
	if err != nil {
		return nil, err
	}
	allocations, err := s.loadAllocations(ctx, profileID)
	if err != nil {
		return nil, err
	}
	savingsSources, err := s.loadSavingsSources(ctx, profileID)
	if err != nil {
		return nil, err
	}
	activeFixedExpenses, err := s.loadActiveFixedExpenses(ctx, profileID)
	if err != nil {
		return nil, err
	}
	incomeSources, err := s.loadIncomeSources(ctx, profileID)
	if err != nil {
		return nil, err
	}
	incomeEntries, err := s.loadIncomeEntries(ctx, period.ID)
	if err != nil {
		return nil, err
	}
	paymentMethods, err := s.loadPaymentMethods(ctx, profileID)
	if err != nil {
		return nil, err
	}
	transactions, err := s.loadTransactions(ctx, period.ID, incomeCategoryID, hasIncomeCategory)
	if err != nil {
		return nil, err
	}

	return &expenseSummaryData{
		period:              period,
		people:              people,
		allocations:         allocations,
		savingsSources:      savingsSources,
		activeFixedExpenses: activeFixedExpenses,
		incomeSources:       incomeSources,
		incomeEntries:       incomeEntries,
		paymentMethods:      paymentMethods,
		transactions:        transactions,
		incomeCategoryID:    incomeCategoryID,
		hasIncomeCategory:   hasIncomeCategory,
		savingsCategoryID:   savingsCategoryID,
		hasSavingsCategory:  hasSavingsCategory,
	}, nil
}

func (s *ExpenseSummaryService) loadPeriod(ctx context.Context, periodID uuid.UUID) (db.BudgetPeriod, error) {
	period, err := s.profiles.GetPeriodByID(ctx, periodID)
	if err != nil {
		s.log.Error("expense_summary.loadPeriod: get period", zap.String("period_id", periodID.String()), zap.Error(err))
		return db.BudgetPeriod{}, err
	}
	return period, nil
}

func (s *ExpenseSummaryService) loadPeople(ctx context.Context, profileID uuid.UUID) ([]db.BudgetToProfileMapping, error) {
	people, err := s.profiles.ListPeople(ctx, profileID)
	if err != nil {
		s.log.Error("expense_summary.loadPeople: list people", zap.String("profile_id", profileID.String()), zap.Error(err))
		return nil, err
	}
	return people, nil
}

func (s *ExpenseSummaryService) loadAllocations(ctx context.Context, profileID uuid.UUID) ([]db.ExpenseAllocation, error) {
	allocations, err := s.allocations.List(ctx, profileID)
	if err != nil {
		s.log.Error("expense_summary.loadAllocations: list expense allocations", zap.String("profile_id", profileID.String()), zap.Error(err))
		return nil, err
	}
	return allocations, nil
}

func (s *ExpenseSummaryService) loadSavingsSources(ctx context.Context, profileID uuid.UUID) ([]db.SavingsSource, error) {
	sources, err := s.profiles.ListSavingsSources(ctx, profileID)
	if err != nil {
		s.log.Error("expense_summary.loadSavingsSources: list savings sources", zap.String("profile_id", profileID.String()), zap.Error(err))
		return nil, err
	}
	return sources, nil
}

func (s *ExpenseSummaryService) loadActiveFixedExpenses(ctx context.Context, profileID uuid.UUID) ([]db.FixedExpense, error) {
	all, err := s.fixedExpenses.List(ctx, profileID)
	if err != nil {
		s.log.Error("expense_summary.loadActiveFixedExpenses: list fixed expenses", zap.String("profile_id", profileID.String()), zap.Error(err))
		return nil, err
	}
	active := make([]db.FixedExpense, 0, len(all))
	for _, fe := range all {
		if fe.IsActive {
			active = append(active, fe)
		}
	}
	return active, nil
}

func (s *ExpenseSummaryService) loadIncomeSources(ctx context.Context, profileID uuid.UUID) ([]db.IncomeSource, error) {
	sources, err := s.profiles.ListIncomeSources(ctx, profileID)
	if err != nil {
		s.log.Error("expense_summary.loadIncomeSources: list income sources", zap.String("profile_id", profileID.String()), zap.Error(err))
		return nil, err
	}
	return sources, nil
}

func (s *ExpenseSummaryService) loadIncomeEntries(ctx context.Context, periodID uuid.UUID) ([]db.IncomeEntry, error) {
	entries, err := s.profiles.ListIncomeEntries(ctx, periodID)
	if err != nil {
		s.log.Error("expense_summary.loadIncomeEntries: list income entries", zap.String("period_id", periodID.String()), zap.Error(err))
		return nil, err
	}
	return entries, nil
}

func (s *ExpenseSummaryService) loadPaymentMethods(ctx context.Context, profileID uuid.UUID) ([]db.ListPaymentMethodsRow, error) {
	methods, err := s.transactions.ListPaymentMethods(ctx, profileID)
	if err != nil {
		s.log.Error("expense_summary.loadPaymentMethods: list payment methods", zap.String("profile_id", profileID.String()), zap.Error(err))
		return nil, err
	}
	return methods, nil
}

// loadSystemCategoryIDs resolves the "Income" and "Savings" system category
// IDs, needed respectively for transaction exclusion and the Savings
// special-case. Either may legitimately not exist (not yet seeded in a
// fresh environment) — that's reported via the hasX bool, not an error.
func (s *ExpenseSummaryService) loadSystemCategoryIDs(ctx context.Context) (incomeCategoryID int32, hasIncome bool, savingsCategoryID int32, hasSavings bool, err error) {
	categories, err := s.transactions.ListSystemCategories(ctx)
	if err != nil {
		s.log.Error("expense_summary.loadSystemCategoryIDs: list system categories", zap.Error(err))
		return 0, false, 0, false, err
	}
	incomeCategoryID, hasIncome = categories["Income"]
	savingsCategoryID, hasSavings = categories["Savings"]
	return incomeCategoryID, hasIncome, savingsCategoryID, hasSavings, nil
}

// loadTransactions fetches the period's transactions and applies the same
// exclusion rule as web's isTransactionExcluded/iOS's
// ExpenseOverviewCalculations.isTransactionExcluded: is_excluded, or the
// Income system category (payroll deposits aren't spend). Every other
// method in this file can assume the transactions it sees already passed
// this filter.
func (s *ExpenseSummaryService) loadTransactions(ctx context.Context, periodID uuid.UUID, incomeCategoryID int32, hasIncomeCategory bool) ([]db.Transaction, error) {
	raw, err := s.transactions.List(ctx, db.ListTransactionsParams{BudgetPeriodID: periodID})
	if err != nil {
		s.log.Error("expense_summary.loadTransactions: list transactions", zap.String("period_id", periodID.String()), zap.Error(err))
		return nil, err
	}
	filtered := make([]db.Transaction, 0, len(raw))
	for _, tx := range raw {
		if tx.IsExcluded {
			continue
		}
		if hasIncomeCategory && tx.CategoryID != nil && *tx.CategoryID == incomeCategoryID {
			continue
		}
		filtered = append(filtered, tx)
	}
	return filtered, nil
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
