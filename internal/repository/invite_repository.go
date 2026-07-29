package repository

import (
	"context"
	"errors"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
)

type InviteRepository interface {
	Create(ctx context.Context, arg db.CreateInviteParams) (db.BudgetInvite, error)
	GetByToken(ctx context.Context, token uuid.UUID) (db.GetInviteByTokenRow, error)
	GetByID(ctx context.Context, id uuid.UUID) (db.BudgetInvite, error)
	ListByProfile(ctx context.Context, profileID uuid.UUID) ([]db.BudgetInvite, error)
	UpdateStatus(ctx context.Context, arg db.UpdateInviteStatusParams) (db.BudgetInvite, error)
}

type inviteRepository struct {
	q *db.Queries
}

func NewInviteRepository(q *db.Queries) InviteRepository {
	if q == nil {
		panic("NewInviteRepository: q is required")
	}
	return &inviteRepository{q: q}
}

func (r *inviteRepository) Create(ctx context.Context, arg db.CreateInviteParams) (db.BudgetInvite, error) {
	return r.q.CreateInvite(ctx, arg)
}

func (r *inviteRepository) GetByToken(ctx context.Context, token uuid.UUID) (db.GetInviteByTokenRow, error) {
	row, err := r.q.GetInviteByToken(ctx, token)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.GetInviteByTokenRow{}, apperr.NotFound("invite", token.String())
	}
	return row, err
}

func (r *inviteRepository) GetByID(ctx context.Context, id uuid.UUID) (db.BudgetInvite, error) {
	inv, err := r.q.GetInviteByID(ctx, id)
	if errors.Is(err, pgx.ErrNoRows) {
		return db.BudgetInvite{}, apperr.NotFound("invite", id.String())
	}
	return inv, err
}

func (r *inviteRepository) ListByProfile(ctx context.Context, profileID uuid.UUID) ([]db.BudgetInvite, error) {
	return r.q.ListInvitesByProfile(ctx, profileID)
}

func (r *inviteRepository) UpdateStatus(ctx context.Context, arg db.UpdateInviteStatusParams) (db.BudgetInvite, error) {
	return r.q.UpdateInviteStatus(ctx, arg)
}
