package repository

import (
	"context"
	"errors"
	"fmt"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

type TransactionRepository interface {
	List(ctx context.Context, arg db.ListTransactionsParams) ([]db.Transaction, error)
	ListFixedRecurring(ctx context.Context, periodID uuid.UUID) ([]db.Transaction, error)
	GetByID(ctx context.Context, id uuid.UUID) (db.Transaction, error)
	Create(ctx context.Context, arg db.CreateTransactionParams) (db.Transaction, error)
	Update(ctx context.Context, arg db.UpdateTransactionParams) (db.Transaction, error)
	Delete(ctx context.Context, arg db.DeleteTransactionParams) error

	GetCategory(ctx context.Context, id int32) (db.GetCategoryRow, error)
	ListCategories(ctx context.Context, userID uuid.UUID) ([]db.ListCategoriesRow, error)
	CreateCategory(ctx context.Context, arg db.CreateCategoryParams) (db.CreateCategoryRow, error)
	UpdateCategory(ctx context.Context, arg db.UpdateCategoryParams) (db.UpdateCategoryRow, error)
	UpdateSystemCategoryColor(ctx context.Context, arg db.UpdateSystemCategoryColorParams) (db.UpdateSystemCategoryColorRow, error)
	DeleteCategoryAndReassign(ctx context.Context, arg db.DeleteCategoryAndReassignParams) error

	GetPaymentMethod(ctx context.Context, id uuid.UUID) (db.PaymentMethod, error)
	ListPaymentMethods(ctx context.Context, budgetProfileID uuid.UUID) ([]db.ListPaymentMethodsRow, error)
	CreatePaymentMethod(ctx context.Context, arg db.CreatePaymentMethodParams) (db.PaymentMethod, error)
	UpdatePaymentMethod(ctx context.Context, arg db.UpdatePaymentMethodParams) (db.PaymentMethod, error)
	DeletePaymentMethodAndReassign(ctx context.Context, arg db.DeletePaymentMethodAndReassignParams) error
}

type transactionRepository struct {
	q *db.Queries
}

func NewTransactionRepository(q *db.Queries) TransactionRepository {
	return &transactionRepository{q: q}
}

func (r *transactionRepository) List(ctx context.Context, arg db.ListTransactionsParams) ([]db.Transaction, error) {
	return r.q.ListTransactions(ctx, arg)
}

func (r *transactionRepository) ListFixedRecurring(ctx context.Context, periodID uuid.UUID) ([]db.Transaction, error) {
	return r.q.ListFixedRecurringTransactionsByPeriod(ctx, &periodID)
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
