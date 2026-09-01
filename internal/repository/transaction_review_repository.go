package repository

import (
	"context"
	"fmt"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
)

type TransactionReviewRepository interface {
	Create(ctx context.Context, periodID, transactionID, matchedTransactionID uuid.UUID, score float64) (db.TransactionReview, error)
	Upsert(ctx context.Context, periodID, transactionID, matchedTransactionID uuid.UUID, score float64) (db.TransactionReview, error)
	List(ctx context.Context, budgetProfileID uuid.UUID) ([]db.ListTransactionReviewsRow, error)
	GetByID(ctx context.Context, id uuid.UUID) (db.TransactionReview, error)
	UpdateStatus(ctx context.Context, id uuid.UUID, status string) error
	GetConfirmedByMatchedTransaction(ctx context.Context, matchedTransactionID uuid.UUID) (db.TransactionReview, error)
	GetByTransactionID(ctx context.Context, transactionID uuid.UUID) (db.TransactionReview, error)
	ResetByMatchedTransaction(ctx context.Context, matchedTransactionID uuid.UUID) error
	CreateAlias(ctx context.Context, fixedExpenseID uuid.UUID, alias string) error
	DeleteAlias(ctx context.Context, fixedExpenseID uuid.UUID, alias string) error
	ListAliases(ctx context.Context, fixedExpenseID uuid.UUID) ([]string, error)
	GetFixedExpenseByAlias(ctx context.Context, alias string, budgetProfileID uuid.UUID) (db.GetFixedExpenseByAliasRow, error)
}

type transactionReviewRepository struct {
	q *db.Queries
}

func NewTransactionReviewRepository(q *db.Queries) TransactionReviewRepository {
	if q == nil {
		panic("NewTransactionReviewRepository: q is required")
	}
	return &transactionReviewRepository{q: q}
}

func (r *transactionReviewRepository) Create(ctx context.Context, periodID, transactionID, matchedTransactionID uuid.UUID, score float64) (db.TransactionReview, error) {
	var scoreNum pgtype.Numeric
	if err := scoreNum.Scan(fmt.Sprintf("%.2f", score)); err != nil {
		return db.TransactionReview{}, err
	}
	row, err := r.q.CreateTransactionReview(ctx, db.CreateTransactionReviewParams{
		BudgetPeriodID:       periodID,
		TransactionID:        transactionID,
		MatchedTransactionID: matchedTransactionID,
		MatchScore:           scoreNum,
	})
	if err != nil {
		return db.TransactionReview{}, err
	}
	return db.TransactionReview{
		ID: row.ID, BudgetPeriodID: row.BudgetPeriodID, TransactionID: row.TransactionID,
		MatchedTransactionID: row.MatchedTransactionID, MatchScore: row.MatchScore,
		Status: row.Status, CreatedAt: row.CreatedAt,
	}, nil
}

func (r *transactionReviewRepository) Upsert(ctx context.Context, periodID, transactionID, matchedTransactionID uuid.UUID, score float64) (db.TransactionReview, error) {
	var scoreNum pgtype.Numeric
	if err := scoreNum.Scan(fmt.Sprintf("%.2f", score)); err != nil {
		return db.TransactionReview{}, err
	}
	row, err := r.q.UpsertTransactionReview(ctx, db.UpsertTransactionReviewParams{
		BudgetPeriodID:       periodID,
		TransactionID:        transactionID,
		MatchedTransactionID: matchedTransactionID,
		MatchScore:           scoreNum,
	})
	if err != nil {
		return db.TransactionReview{}, err
	}
	return db.TransactionReview{
		ID: row.ID, BudgetPeriodID: row.BudgetPeriodID, TransactionID: row.TransactionID,
		MatchedTransactionID: row.MatchedTransactionID, MatchScore: row.MatchScore,
		Status: row.Status, CreatedAt: row.CreatedAt,
	}, nil
}

func (r *transactionReviewRepository) List(ctx context.Context, budgetProfileID uuid.UUID) ([]db.ListTransactionReviewsRow, error) {
	return r.q.ListTransactionReviews(ctx, budgetProfileID)
}

func (r *transactionReviewRepository) GetByID(ctx context.Context, id uuid.UUID) (db.TransactionReview, error) {
	row, err := r.q.GetTransactionReview(ctx, id)
	if err == pgx.ErrNoRows {
		return db.TransactionReview{}, apperr.NotFound("transaction_review", id.String())
	}
	if err != nil {
		return db.TransactionReview{}, err
	}
	return db.TransactionReview{
		ID: row.ID, BudgetPeriodID: row.BudgetPeriodID, TransactionID: row.TransactionID,
		MatchedTransactionID: row.MatchedTransactionID, MatchScore: row.MatchScore,
		Status: row.Status, CreatedAt: row.CreatedAt,
	}, nil
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

func (r *transactionReviewRepository) GetConfirmedByMatchedTransaction(ctx context.Context, matchedTransactionID uuid.UUID) (db.TransactionReview, error) {
	row, err := r.q.GetConfirmedReviewByMatchedTransaction(ctx, matchedTransactionID)
	if err == pgx.ErrNoRows {
		return db.TransactionReview{}, apperr.NotFound("transaction_review", matchedTransactionID.String())
	}
	if err != nil {
		return db.TransactionReview{}, err
	}
	return db.TransactionReview{
		ID:                   row.ID,
		BudgetPeriodID:       row.BudgetPeriodID,
		TransactionID:        row.TransactionID,
		MatchedTransactionID: row.MatchedTransactionID,
		MatchScore:           row.MatchScore,
		Status:               row.Status,
		CreatedAt:            row.CreatedAt,
	}, nil
}

func (r *transactionReviewRepository) GetByTransactionID(ctx context.Context, transactionID uuid.UUID) (db.TransactionReview, error) {
	row, err := r.q.GetTransactionReviewByTransactionID(ctx, transactionID)
	if err == pgx.ErrNoRows {
		return db.TransactionReview{}, apperr.NotFound("transaction_review", transactionID.String())
	}
	if err != nil {
		return db.TransactionReview{}, err
	}
	return db.TransactionReview{
		ID:                   row.ID,
		BudgetPeriodID:       row.BudgetPeriodID,
		TransactionID:        row.TransactionID,
		MatchedTransactionID: row.MatchedTransactionID,
		MatchScore:           row.MatchScore,
		Status:               row.Status,
		CreatedAt:            row.CreatedAt,
	}, nil
}

func (r *transactionReviewRepository) ResetByMatchedTransaction(ctx context.Context, matchedTransactionID uuid.UUID) error {
	return r.q.ResetConfirmedReviewByMatchedTransaction(ctx, matchedTransactionID)
}

func (r *transactionReviewRepository) DeleteAlias(ctx context.Context, fixedExpenseID uuid.UUID, alias string) error {
	return r.q.DeleteFixedExpenseAlias(ctx, db.DeleteFixedExpenseAliasParams{
		FixedExpenseID: fixedExpenseID,
		Alias:          alias,
	})
}
