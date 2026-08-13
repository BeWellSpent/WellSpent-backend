package repository

import (
	"context"
	"errors"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
)

type PlaidRepository interface {
	Create(ctx context.Context, arg db.CreatePlaidItemParams) (db.PlaidItem, error)
	GetByID(ctx context.Context, id uuid.UUID) (db.PlaidItem, error)
	GetByItemID(ctx context.Context, itemID string) (db.PlaidItem, error)
	ListByUser(ctx context.Context, userID uuid.UUID) ([]db.PlaidItem, error)
	ListByBudgetProfile(ctx context.Context, profileID uuid.UUID) ([]db.PlaidItem, error)
	ListActiveForSync(ctx context.Context) ([]db.PlaidItem, error)
	ListUnsyncableForUser(ctx context.Context, userID uuid.UUID) ([]db.ListUnsyncableConnectionsForUserRow, error)
	UpdateStatus(ctx context.Context, arg db.UpdatePlaidItemStatusParams) (db.PlaidItem, error)
	UpdateSync(ctx context.Context, arg db.UpdatePlaidItemSyncParams) (db.PlaidItem, error)
	Delete(ctx context.Context, id uuid.UUID) error
}

type plaidRepository struct {
	q *db.Queries
}

func NewPlaidRepository(q *db.Queries) PlaidRepository {
	if q == nil {
		panic("NewPlaidRepository: q is required")
	}
	return &plaidRepository{q: q}
}

func (r *plaidRepository) Create(ctx context.Context, arg db.CreatePlaidItemParams) (db.PlaidItem, error) {
	return r.q.CreatePlaidItem(ctx, arg)
}

func (r *plaidRepository) GetByID(ctx context.Context, id uuid.UUID) (db.PlaidItem, error) {
	item, err := r.q.GetPlaidItemByID(ctx, id)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.PlaidItem{}, apperr.NotFound("plaid_item", id.String())
	}
	return item, err
}

func (r *plaidRepository) GetByItemID(ctx context.Context, itemID string) (db.PlaidItem, error) {
	item, err := r.q.GetPlaidItemByItemID(ctx, itemID)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.PlaidItem{}, apperr.NotFound("plaid_item", itemID)
	}
	return item, err
}

func (r *plaidRepository) ListByUser(ctx context.Context, userID uuid.UUID) ([]db.PlaidItem, error) {
	return r.q.ListPlaidItemsByUser(ctx, userID)
}

func (r *plaidRepository) ListByBudgetProfile(ctx context.Context, profileID uuid.UUID) ([]db.PlaidItem, error) {
	return r.q.ListPlaidItemsByBudgetProfile(ctx, profileID)
}

func (r *plaidRepository) ListActiveForSync(ctx context.Context) ([]db.PlaidItem, error) {
	return r.q.ListActivePlaidItemsForSync(ctx)
}

func (r *plaidRepository) ListUnsyncableForUser(ctx context.Context, userID uuid.UUID) ([]db.ListUnsyncableConnectionsForUserRow, error) {
	return r.q.ListUnsyncableConnectionsForUser(ctx, userID)
}

func (r *plaidRepository) UpdateStatus(ctx context.Context, arg db.UpdatePlaidItemStatusParams) (db.PlaidItem, error) {
	return r.q.UpdatePlaidItemStatus(ctx, arg)
}

func (r *plaidRepository) UpdateSync(ctx context.Context, arg db.UpdatePlaidItemSyncParams) (db.PlaidItem, error) {
	return r.q.UpdatePlaidItemSync(ctx, arg)
}

func (r *plaidRepository) Delete(ctx context.Context, id uuid.UUID) error {
	return r.q.DeletePlaidItem(ctx, id)
}
