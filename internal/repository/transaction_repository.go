package repository

import (
	"context"
	"errors"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

type TransactionRepository interface {
	List(ctx context.Context, arg db.ListTransactionsParams) ([]db.Transaction, error)
	GetByID(ctx context.Context, id uuid.UUID) (db.Transaction, error)
	Create(ctx context.Context, arg db.CreateTransactionParams) (db.Transaction, error)
	Update(ctx context.Context, arg db.UpdateTransactionParams) (db.Transaction, error)
	Delete(ctx context.Context, arg db.DeleteTransactionParams) error

	ListCategories(ctx context.Context, userID uuid.UUID) ([]db.Category, error)
	CreateCategory(ctx context.Context, arg db.CreateCategoryParams) (db.Category, error)

	ListPaymentMethods(ctx context.Context, userID uuid.UUID) ([]db.ListPaymentMethodsRow, error)
	CreatePaymentMethod(ctx context.Context, arg db.CreatePaymentMethodParams) (db.PaymentMethod, error)
	UpdatePaymentMethod(ctx context.Context, arg db.UpdatePaymentMethodParams) (db.PaymentMethod, error)
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

func (r *transactionRepository) ListCategories(ctx context.Context, userID uuid.UUID) ([]db.Category, error) {
	return r.q.ListCategories(ctx, userID)
}

func (r *transactionRepository) CreateCategory(ctx context.Context, arg db.CreateCategoryParams) (db.Category, error) {
	return r.q.CreateCategory(ctx, arg)
}

func (r *transactionRepository) ListPaymentMethods(ctx context.Context, userID uuid.UUID) ([]db.ListPaymentMethodsRow, error) {
	return r.q.ListPaymentMethods(ctx, userID)
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
