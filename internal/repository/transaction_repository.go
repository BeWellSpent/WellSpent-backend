package repository

import (
	"context"
	"errors"
	"fmt"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/category"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
)

type TransactionRepository interface {
	List(ctx context.Context, arg db.ListTransactionsParams) ([]db.Transaction, error)
	GetByID(ctx context.Context, id uuid.UUID) (db.Transaction, error)
	Create(ctx context.Context, arg db.CreateTransactionParams) (db.Transaction, error)
	Update(ctx context.Context, arg db.UpdateTransactionParams) (db.Transaction, error)
	Delete(ctx context.Context, arg db.DeleteTransactionParams) error
	MarkAsPaid(ctx context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error)
	UnmarkAsPaid(ctx context.Context, arg db.UnmarkTransactionAsPaidParams) (db.Transaction, error)
	SetExcluded(ctx context.Context, arg db.SetTransactionExcludedParams) (db.Transaction, error)
	SetInstallmentPlan(ctx context.Context, arg db.SetTransactionInstallmentPlanParams) (db.Transaction, error)
	ClearInstallmentPlan(ctx context.Context, arg db.ClearTransactionInstallmentPlanParams) (db.Transaction, error)
	ListByFixedExpense(ctx context.Context, fixedExpenseID uuid.UUID) ([]db.Transaction, error)
	CountCarried(ctx context.Context, arg db.CountCarriedTransactionsParams) (int64, error)
	DeleteByFixedExpense(ctx context.Context, fixedExpenseID uuid.UUID) error

	GetCategory(ctx context.Context, id int32) (db.GetCategoryRow, error)
	ListCategories(ctx context.Context, userID uuid.UUID) ([]db.ListCategoriesRow, error)
	ListCategoriesForBudget(ctx context.Context, userID uuid.UUID, budgetProfileID uuid.UUID) ([]db.ListCategoriesRow, error)
	CreateCategory(ctx context.Context, arg db.CreateCategoryParams) (db.CreateCategoryRow, error)
	UpdateCategory(ctx context.Context, arg db.UpdateCategoryParams) (db.UpdateCategoryRow, error)
	UpdateSystemCategoryColor(ctx context.Context, arg db.UpdateSystemCategoryColorParams) (db.UpdateSystemCategoryColorRow, error)
	DeleteCategoryAndReassign(ctx context.Context, arg db.DeleteCategoryAndReassignParams) error

	GetPaymentMethod(ctx context.Context, id uuid.UUID) (db.PaymentMethod, error)
	ListPaymentMethods(ctx context.Context, budgetProfileID uuid.UUID) ([]db.ListPaymentMethodsRow, error)
	CreatePaymentMethod(ctx context.Context, arg db.CreatePaymentMethodParams) (db.PaymentMethod, error)
	UpdatePaymentMethod(ctx context.Context, arg db.UpdatePaymentMethodParams) (db.PaymentMethod, error)
	DeletePaymentMethodAndReassign(ctx context.Context, arg db.DeletePaymentMethodAndReassignParams) error
	DeleteSavingsSourceTransactions(ctx context.Context, arg db.DeleteSavingsSourceTransactionsParams) error

	ListSystemCategories(ctx context.Context) (map[category.Key]int32, error)

	// Plaid
	CreateTransactionFromPlaid(ctx context.Context, arg db.CreateTransactionFromPlaidParams) (db.Transaction, error)
	ExistsTransactionByPlaidID(ctx context.Context, plaidTransactionID *string) (bool, error)
	UpdateTransactionFromPlaid(ctx context.Context, arg db.UpdateTransactionFromPlaidParams) error
	DeleteTransactionByPlaidID(ctx context.Context, plaidTransactionID *string) error
	CreatePaymentMethodFromPlaid(ctx context.Context, arg db.CreatePaymentMethodFromPlaidParams) (db.PaymentMethod, error)
	GetPaymentMethodByPlaidAccountID(ctx context.Context, plaidAccountID string) (db.PaymentMethod, error)
	GetPaymentMethodByUserAndName(ctx context.Context, userID uuid.UUID, name string) (db.PaymentMethod, error)
	UpdatePaymentMethodPlaidAccountID(ctx context.Context, id uuid.UUID, plaidAccountID string) error
	ListActivePaymentMethodsByPlaidItem(ctx context.Context, plaidItemID uuid.UUID) ([]db.PaymentMethod, error)
	DeactivatePaymentMethod(ctx context.Context, id uuid.UUID) error
}

type transactionRepository struct {
	q *db.Queries
}

func NewTransactionRepository(q *db.Queries) TransactionRepository {
	if q == nil {
		panic("NewTransactionRepository: q is required")
	}
	return &transactionRepository{q: q}
}

func (r *transactionRepository) List(ctx context.Context, arg db.ListTransactionsParams) ([]db.Transaction, error) {
	return r.q.ListTransactions(ctx, arg)
}

func (r *transactionRepository) GetByID(ctx context.Context, id uuid.UUID) (db.Transaction, error) {
	t, err := r.q.GetTransactionByID(ctx, id)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.Transaction{}, apperr.NotFound("transaction", id.String())
	}
	return t, err
}

func (r *transactionRepository) Create(ctx context.Context, arg db.CreateTransactionParams) (db.Transaction, error) {
	return r.q.CreateTransaction(ctx, arg)
}

func (r *transactionRepository) Update(ctx context.Context, arg db.UpdateTransactionParams) (db.Transaction, error) {
	t, err := r.q.UpdateTransaction(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.Transaction{}, apperr.NotFound("transaction", arg.ID.String())
	}
	return t, err
}

func (r *transactionRepository) Delete(ctx context.Context, arg db.DeleteTransactionParams) error {
	return r.q.DeleteTransaction(ctx, arg)
}

func (r *transactionRepository) MarkAsPaid(ctx context.Context, arg db.MarkTransactionAsPaidParams) (db.Transaction, error) {
	t, err := r.q.MarkTransactionAsPaid(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.Transaction{}, apperr.NotFound("transaction", arg.ID.String())
	}
	return t, err
}

func (r *transactionRepository) UnmarkAsPaid(ctx context.Context, arg db.UnmarkTransactionAsPaidParams) (db.Transaction, error) {
	t, err := r.q.UnmarkTransactionAsPaid(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.Transaction{}, apperr.NotFound("transaction", arg.ID.String())
	}
	return t, err
}

func (r *transactionRepository) SetExcluded(ctx context.Context, arg db.SetTransactionExcludedParams) (db.Transaction, error) {
	t, err := r.q.SetTransactionExcluded(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.Transaction{}, apperr.NotFound("transaction", arg.ID.String())
	}
	return t, err
}

func (r *transactionRepository) SetInstallmentPlan(ctx context.Context, arg db.SetTransactionInstallmentPlanParams) (db.Transaction, error) {
	t, err := r.q.SetTransactionInstallmentPlan(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.Transaction{}, apperr.NotFound("transaction", arg.ID.String())
	}
	return t, err
}

func (r *transactionRepository) ClearInstallmentPlan(ctx context.Context, arg db.ClearTransactionInstallmentPlanParams) (db.Transaction, error) {
	t, err := r.q.ClearTransactionInstallmentPlan(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.Transaction{}, apperr.NotFound("transaction", arg.ID.String())
	}
	return t, err
}

func (r *transactionRepository) ListByFixedExpense(ctx context.Context, fixedExpenseID uuid.UUID) ([]db.Transaction, error) {
	return r.q.ListTransactionsByFixedExpense(ctx, fixedExpenseID)
}

func (r *transactionRepository) DeleteByFixedExpense(ctx context.Context, fixedExpenseID uuid.UUID) error {
	return r.q.DeleteTransactionsByFixedExpense(ctx, fixedExpenseID)
}

func (r *transactionRepository) CountCarried(ctx context.Context, arg db.CountCarriedTransactionsParams) (int64, error) {
	return r.q.CountCarriedTransactions(ctx, arg)
}

func (r *transactionRepository) GetCategory(ctx context.Context, id int32) (db.GetCategoryRow, error) {
	c, err := r.q.GetCategory(ctx, id)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.GetCategoryRow{}, apperr.NotFound("category", fmt.Sprintf("%d", id))
	}
	return c, err
}

func (r *transactionRepository) ListCategories(ctx context.Context, userID uuid.UUID) ([]db.ListCategoriesRow, error) {
	return r.q.ListCategories(ctx, userID)
}

func (r *transactionRepository) ListCategoriesForBudget(ctx context.Context, userID uuid.UUID, budgetProfileID uuid.UUID) ([]db.ListCategoriesRow, error) {
	rows, err := r.q.ListCategoriesForBudget(ctx, db.ListCategoriesForBudgetParams{
		UserID:          userID,
		BudgetProfileID: budgetProfileID,
	})
	if err != nil {
		return nil, err
	}
	result := make([]db.ListCategoriesRow, len(rows))
	for i, r := range rows {
		result[i] = db.ListCategoriesRow{
			ID: r.ID, Name: r.Name, TypeID: r.TypeID,
			IsSystem: r.IsSystem, UserID: r.UserID, Color: r.Color,
			SystemKey: r.SystemKey,
		}
	}
	return result, nil
}

func (r *transactionRepository) CreateCategory(ctx context.Context, arg db.CreateCategoryParams) (db.CreateCategoryRow, error) {
	return r.q.CreateCategory(ctx, arg)
}

func (r *transactionRepository) UpdateCategory(ctx context.Context, arg db.UpdateCategoryParams) (db.UpdateCategoryRow, error) {
	c, err := r.q.UpdateCategory(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.UpdateCategoryRow{}, apperr.NotFound("category", fmt.Sprintf("%d", arg.ID))
	}
	return c, err
}

func (r *transactionRepository) UpdateSystemCategoryColor(ctx context.Context, arg db.UpdateSystemCategoryColorParams) (db.UpdateSystemCategoryColorRow, error) {
	c, err := r.q.UpdateSystemCategoryColor(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.UpdateSystemCategoryColorRow{}, apperr.NotFound("category", fmt.Sprintf("%d", arg.ID))
	}
	return c, err
}

func (r *transactionRepository) DeleteCategoryAndReassign(ctx context.Context, arg db.DeleteCategoryAndReassignParams) error {
	return r.q.DeleteCategoryAndReassign(ctx, arg)
}

func (r *transactionRepository) ListPaymentMethods(ctx context.Context, budgetProfileID uuid.UUID) ([]db.ListPaymentMethodsRow, error) {
	return r.q.ListPaymentMethods(ctx, budgetProfileID)
}

func (r *transactionRepository) CreatePaymentMethod(ctx context.Context, arg db.CreatePaymentMethodParams) (db.PaymentMethod, error) {
	return r.q.CreatePaymentMethod(ctx, arg)
}

func (r *transactionRepository) UpdatePaymentMethod(ctx context.Context, arg db.UpdatePaymentMethodParams) (db.PaymentMethod, error) {
	m, err := r.q.UpdatePaymentMethod(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.PaymentMethod{}, apperr.NotFound("payment_method", arg.ID.String())
	}
	return m, err
}

func (r *transactionRepository) GetPaymentMethod(ctx context.Context, id uuid.UUID) (db.PaymentMethod, error) {
	m, err := r.q.GetPaymentMethod(ctx, id)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.PaymentMethod{}, apperr.NotFound("payment_method", id.String())
	}
	return m, err
}

func (r *transactionRepository) DeletePaymentMethodAndReassign(ctx context.Context, arg db.DeletePaymentMethodAndReassignParams) error {
	return r.q.DeletePaymentMethodAndReassign(ctx, arg)
}

func (r *transactionRepository) DeleteSavingsSourceTransactions(ctx context.Context, arg db.DeleteSavingsSourceTransactionsParams) error {
	return r.q.DeleteSavingsSourceTransactions(ctx, arg)
}

// ListSystemCategories maps each seeded system category's stable key to its
// ID. Keyed on system_key rather than the English name (issue #49) so callers
// keep working when the name becomes a localized display string.
//
// A system category with no key is skipped rather than falling back to its
// name: a nil key means migration 000054's backfill missed a row, and silently
// keying that row by name would hide the gap behind a lookup that happens to
// still work in English.
func (r *transactionRepository) ListSystemCategories(ctx context.Context) (map[category.Key]int32, error) {
	rows, err := r.q.ListSystemCategories(ctx)
	if err != nil {
		return nil, err
	}
	m := make(map[category.Key]int32, len(rows))
	for _, row := range rows {
		if row.SystemKey == nil {
			continue
		}
		m[category.Key(*row.SystemKey)] = row.ID
	}
	return m, nil
}

func (r *transactionRepository) CreateTransactionFromPlaid(ctx context.Context, arg db.CreateTransactionFromPlaidParams) (db.Transaction, error) {
	return r.q.CreateTransactionFromPlaid(ctx, arg)
}

func (r *transactionRepository) ExistsTransactionByPlaidID(ctx context.Context, plaidTransactionID *string) (bool, error) {
	return r.q.ExistsTransactionByPlaidID(ctx, plaidTransactionID)
}

func (r *transactionRepository) UpdateTransactionFromPlaid(ctx context.Context, arg db.UpdateTransactionFromPlaidParams) error {
	return r.q.UpdateTransactionFromPlaid(ctx, arg)
}

func (r *transactionRepository) DeleteTransactionByPlaidID(ctx context.Context, plaidTransactionID *string) error {
	return r.q.DeleteTransactionByPlaidID(ctx, plaidTransactionID)
}

func (r *transactionRepository) CreatePaymentMethodFromPlaid(ctx context.Context, arg db.CreatePaymentMethodFromPlaidParams) (db.PaymentMethod, error) {
	return r.q.CreatePaymentMethodFromPlaid(ctx, arg)
}

func (r *transactionRepository) GetPaymentMethodByPlaidAccountID(ctx context.Context, plaidAccountID string) (db.PaymentMethod, error) {
	m, err := r.q.GetPaymentMethodByPlaidAccountID(ctx, &plaidAccountID)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.PaymentMethod{}, apperr.NotFound("payment_method", plaidAccountID)
	}
	return m, err
}

func (r *transactionRepository) GetPaymentMethodByUserAndName(ctx context.Context, userID uuid.UUID, name string) (db.PaymentMethod, error) {
	m, err := r.q.GetPaymentMethodByUserAndName(ctx, db.GetPaymentMethodByUserAndNameParams{
		UserID: &userID,
		Name:   name,
	})
	if errors.Is(err, pgx.ErrNoRows) {
		return db.PaymentMethod{}, apperr.NotFound("payment_method", name)
	}
	return m, err
}

func (r *transactionRepository) UpdatePaymentMethodPlaidAccountID(ctx context.Context, id uuid.UUID, plaidAccountID string) error {
	return r.q.UpdatePaymentMethodPlaidAccountID(ctx, db.UpdatePaymentMethodPlaidAccountIDParams{
		PlaidAccountID: &plaidAccountID,
		ID:             id,
	})
}

func (r *transactionRepository) ListActivePaymentMethodsByPlaidItem(ctx context.Context, plaidItemID uuid.UUID) ([]db.PaymentMethod, error) {
	return r.q.ListActivePaymentMethodsByPlaidItem(ctx, &plaidItemID)
}

func (r *transactionRepository) DeactivatePaymentMethod(ctx context.Context, id uuid.UUID) error {
	return r.q.DeactivatePaymentMethod(ctx, id)
}
