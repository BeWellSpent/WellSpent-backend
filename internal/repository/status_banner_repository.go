package repository

import (
	"context"
	"errors"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
)

type StatusBannerRepository interface {
	// GetActive returns the single banner clients should show, or a
	// *apperr.NotFoundError when nothing is live. "Nothing to show" is by far
	// the common case here, so callers are expected to treat that error as
	// ordinary rather than exceptional.
	GetActive(ctx context.Context) (db.StatusBanner, error)
	Create(ctx context.Context, arg db.CreateStatusBannerParams) (db.StatusBanner, error)
	List(ctx context.Context, limit int32) ([]db.StatusBanner, error)
	Expire(ctx context.Context, id uuid.UUID) (db.StatusBanner, error)
}

type statusBannerRepository struct {
	q *db.Queries
}

func NewStatusBannerRepository(q *db.Queries) StatusBannerRepository {
	if q == nil {
		panic("NewStatusBannerRepository: q is required")
	}
	return &statusBannerRepository{q: q}
}

func (r *statusBannerRepository) GetActive(ctx context.Context) (db.StatusBanner, error) {
	b, err := r.q.GetActiveStatusBanner(ctx)
	if err != nil {
		if errors.Is(err, pgx.ErrNoRows) {
			return db.StatusBanner{}, apperr.NotFound("status_banner", "active")
		}
		return db.StatusBanner{}, err
	}
	return b, nil
}

func (r *statusBannerRepository) Create(ctx context.Context, arg db.CreateStatusBannerParams) (db.StatusBanner, error) {
	return r.q.CreateStatusBanner(ctx, arg)
}

func (r *statusBannerRepository) List(ctx context.Context, limit int32) ([]db.StatusBanner, error) {
	return r.q.ListStatusBanners(ctx, limit)
}

func (r *statusBannerRepository) Expire(ctx context.Context, id uuid.UUID) (db.StatusBanner, error) {
	b, err := r.q.ExpireStatusBanner(ctx, id)
	if err != nil {
		if errors.Is(err, pgx.ErrNoRows) {
			return db.StatusBanner{}, apperr.NotFound("status_banner", id.String())
		}
		return db.StatusBanner{}, err
	}
	return b, nil
}
