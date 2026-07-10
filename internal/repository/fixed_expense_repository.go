package repository

import (
	"context"
	"errors"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

type FixedExpenseRepository interface {
	Create(ctx context.Context, arg db.CreateFixedExpenseParams) (db.FixedExpense, error)
	GetByID(ctx context.Context, id uuid.UUID) (db.FixedExpense, error)
	List(ctx context.Context, budgetProfileID uuid.UUID) ([]db.FixedExpense, error)
	Update(ctx context.Context, arg db.UpdateFixedExpenseParams) (db.FixedExpense, error)
	UpdatePlannedAmount(ctx context.Context, arg db.UpdateFixedExpensePlannedAmountParams) error
	Deactivate(ctx context.Context, arg db.DeactivateFixedExpenseParams) error
	GetUnpaidTransaction(ctx context.Context, arg db.GetUnpaidTransactionByFixedExpenseParams) (db.Transaction, error)
	DeleteUnpaidTransactions(ctx context.Context, arg db.DeleteUnpaidTransactionByFixedExpenseParams) error
	UpdateTransactionFromFixedExpense(ctx context.Context, arg db.UpdateTransactionFromFixedExpenseParams) error
	HasTransactionInMonth(ctx context.Context, arg db.FixedExpenseHasTransactionInMonthParams) (bool, error)
	HasTransactionOnDate(ctx context.Context, arg db.FixedExpenseHasTransactionOnDateParams) (bool, error)
}

type fixedExpenseRepository struct {
	q *db.Queries
}

func NewFixedExpenseRepository(q *db.Queries) FixedExpenseRepository {
	return &fixedExpenseRepository{q: q}
}

func (r *fixedExpenseRepository) Create(ctx context.Context, arg db.CreateFixedExpenseParams) (db.FixedExpense, error) {
	return r.q.CreateFixedExpense(ctx, arg)
}

func (r *fixedExpenseRepository) GetByID(ctx context.Context, id uuid.UUID) (db.FixedExpense, error) {
	fe, err := r.q.GetFixedExpense(ctx, id)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.FixedExpense{}, apperr.NotFound("fixed_expense", id.String())
	}
	return fe, err
}

func (r *fixedExpenseRepository) List(ctx context.Context, budgetProfileID uuid.UUID) ([]db.FixedExpense, error) {
	return r.q.ListFixedExpenses(ctx, budgetProfileID)
}

func (r *fixedExpenseRepository) Update(ctx context.Context, arg db.UpdateFixedExpenseParams) (db.FixedExpense, error) {
	fe, err := r.q.UpdateFixedExpense(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.FixedExpense{}, apperr.NotFound("fixed_expense", arg.ID.String())
	}
	return fe, err
}

func (r *fixedExpenseRepository) UpdatePlannedAmount(ctx context.Context, arg db.UpdateFixedExpensePlannedAmountParams) error {
	return r.q.UpdateFixedExpensePlannedAmount(ctx, arg)
}

func (r *fixedExpenseRepository) Deactivate(ctx context.Context, arg db.DeactivateFixedExpenseParams) error {
	return r.q.DeactivateFixedExpense(ctx, arg)
}

func (r *fixedExpenseRepository) GetUnpaidTransaction(ctx context.Context, arg db.GetUnpaidTransactionByFixedExpenseParams) (db.Transaction, error) {
	tx, err := r.q.GetUnpaidTransactionByFixedExpense(ctx, arg)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.Transaction{}, apperr.NotFound("transaction", arg.FixedExpenseID.String())
	}
	return tx, err
}

func (r *fixedExpenseRepository) DeleteUnpaidTransactions(ctx context.Context, arg db.DeleteUnpaidTransactionByFixedExpenseParams) error {
	return r.q.DeleteUnpaidTransactionByFixedExpense(ctx, arg)
}

func (r *fixedExpenseRepository) UpdateTransactionFromFixedExpense(ctx context.Context, arg db.UpdateTransactionFromFixedExpenseParams) error {
	return r.q.UpdateTransactionFromFixedExpense(ctx, arg)
}

func (r *fixedExpenseRepository) HasTransactionInMonth(ctx context.Context, arg db.FixedExpenseHasTransactionInMonthParams) (bool, error) {
	return r.q.FixedExpenseHasTransactionInMonth(ctx, arg)
}

func (r *fixedExpenseRepository) HasTransactionOnDate(ctx context.Context, arg db.FixedExpenseHasTransactionOnDateParams) (bool, error) {
	return r.q.FixedExpenseHasTransactionOnDate(ctx, arg)
}
