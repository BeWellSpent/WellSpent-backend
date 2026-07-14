package repository

import (
	"context"
	"fmt"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/mauro-afa91/spendsense/internal/apperr"
	db "github.com/mauro-afa91/spendsense/internal/sqlc"
)

type TransactionReviewRepository interface {
	Create(ctx context.Context, periodID, transactionID, fixedExpenseID uuid.UUID, score float64) (db.TransactionReview, error)
	Upsert(ctx context.Context, periodID, transactionID, fixedExpenseID uuid.UUID, score float64) (db.TransactionReview, error)
	ListPending(ctx context.Context, budgetProfileID uuid.UUID) ([]db.ListPendingTransactionReviewsRow, error)
	GetByID(ctx context.Context, id uuid.UUID) (db.TransactionReview, error)
	UpdateStatus(ctx context.Context, id uuid.UUID, status string) error
	GetConfirmedByFixedExpenseAndPeriod(ctx context.Context, fixedExpenseID, periodID uuid.UUID) (db.TransactionReview, error)
	ResetByFixedExpenseAndPeriod(ctx context.Context, fixedExpenseID, periodID uuid.UUID) error
	CreateAlias(ctx context.Context, fixedExpenseID uuid.UUID, alias string) error
	DeleteAlias(ctx context.Context, fixedExpenseID uuid.UUID, alias string) error
	ListAliases(ctx context.Context, fixedExpenseID uuid.UUID) ([]string, error)
	GetFixedExpenseByAlias(ctx context.Context, alias string, budgetProfileID uuid.UUID) (db.GetFixedExpenseByAliasRow, error)
}

type transactionReviewRepository struct {
	q *db.Queries
}

func NewTransactionReviewRepository(q *db.Queries) TransactionReviewRepository {
	return &transactionReviewRepository{q: q}
}

func (r *transactionReviewRepository) Create(ctx context.Context, periodID, transactionID, fixedExpenseID uuid.UUID, score float64) (db.TransactionReview, error) {
	var scoreNum pgtype.Numeric
	if err := scoreNum.Scan(fmt.Sprintf("%.2f", score)); err != nil {
		return db.TransactionReview{}, err
	}
	return r.q.CreateTransactionReview(ctx, db.CreateTransactionReviewParams{
		BudgetPeriodID: periodID,
		TransactionID:  transactionID,
		FixedExpenseID: fixedExpenseID,
		MatchScore:     scoreNum,
	})
}

func (r *transactionReviewRepository) Upsert(ctx context.Context, periodID, transactionID, fixedExpenseID uuid.UUID, score float64) (db.TransactionReview, error) {
	var scoreNum pgtype.Numeric
	if err := scoreNum.Scan(fmt.Sprintf("%.2f", score)); err != nil {
		return db.TransactionReview{}, err
	}
	return r.q.UpsertTransactionReview(ctx, db.UpsertTransactionReviewParams{
		BudgetPeriodID: periodID,
		TransactionID:  transactionID,
		FixedExpenseID: fixedExpenseID,
		MatchScore:     scoreNum,
	})
}

func (r *transactionReviewRepository) ListPending(ctx context.Context, budgetProfileID uuid.UUID) ([]db.ListPendingTransactionReviewsRow, error) {
	return r.q.ListPendingTransactionReviews(ctx, budgetProfileID)
}

func (r *transactionReviewRepository) GetByID(ctx context.Context, id uuid.UUID) (db.TransactionReview, error) {
	row, err := r.q.GetTransactionReview(ctx, id)
	if err == pgx.ErrNoRows {
		return db.TransactionReview{}, apperr.NotFound("transaction_review", id.String())
	}
	return row, err
}

func (r *transactionReviewRepository) UpdateStatus(ctx context.Context, id uuid.UUID, status string) error {
	return r.q.UpdateTransactionReviewStatus(ctx, db.UpdateTransactionReviewStatusParams{
		ID:     id,
		Status: status,
	})
}

func (r *transactionReviewRepository) CreateAlias(ctx context.Context, fixedExpenseID uuid.UUID, alias string) error {
	return r.q.CreateFixedExpenseAlias(ctx, db.CreateFixedExpenseAliasParams{
		FixedExpenseID: fixedExpenseID,
		Alias:          alias,
	})
}

func (r *transactionReviewRepository) ListAliases(ctx context.Context, fixedExpenseID uuid.UUID) ([]string, error) {
	return r.q.ListFixedExpenseAliases(ctx, fixedExpenseID)
}

func (r *transactionReviewRepository) GetFixedExpenseByAlias(ctx context.Context, alias string, budgetProfileID uuid.UUID) (db.GetFixedExpenseByAliasRow, error) {
	row, err := r.q.GetFixedExpenseByAlias(ctx, db.GetFixedExpenseByAliasParams{
		Alias:           alias,
		BudgetProfileID: budgetProfileID,
	})
	if err == pgx.ErrNoRows {
		return db.GetFixedExpenseByAliasRow{}, apperr.NotFound("fixed_expense_alias", alias)
	}
	return row, err
}

func (r *transactionReviewRepository) GetConfirmedByFixedExpenseAndPeriod(ctx context.Context, fixedExpenseID, periodID uuid.UUID) (db.TransactionReview, error) {
	row, err := r.q.GetConfirmedReviewByFixedExpense(ctx, db.GetConfirmedReviewByFixedExpenseParams{
		FixedExpenseID: fixedExpenseID,
		BudgetPeriodID: periodID,
	})
	if err == pgx.ErrNoRows {
		return db.TransactionReview{}, apperr.NotFound("transaction_review", fixedExpenseID.String())
	}
	return row, err
}

func (r *transactionReviewRepository) ResetByFixedExpenseAndPeriod(ctx context.Context, fixedExpenseID, periodID uuid.UUID) error {
	return r.q.ResetConfirmedReviewByFixedExpense(ctx, db.ResetConfirmedReviewByFixedExpenseParams{
		FixedExpenseID: fixedExpenseID,
		BudgetPeriodID: periodID,
	})
}

func (r *transactionReviewRepository) DeleteAlias(ctx context.Context, fixedExpenseID uuid.UUID, alias string) error {
	return r.q.DeleteFixedExpenseAlias(ctx, db.DeleteFixedExpenseAliasParams{
		FixedExpenseID: fixedExpenseID,
		Alias:          alias,
	})
}
