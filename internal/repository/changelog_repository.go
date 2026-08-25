package repository

import (
	"context"
	"errors"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgconn"
)

type ChangelogRepository interface {
	// ListReleases returns releases for the given components, newest first.
	// An empty slice means every component.
	ListReleases(ctx context.Context, components []string) ([]db.ChangelogRelease, error)
	// ListItems returns every item belonging to the given releases, in one
	// round trip rather than one query per release.
	ListItems(ctx context.Context, releaseIDs []uuid.UUID) ([]db.ChangelogItem, error)
	CreateRelease(ctx context.Context, arg db.CreateChangelogReleaseParams) (db.ChangelogRelease, error)
	CreateItem(ctx context.Context, arg db.CreateChangelogItemParams) (db.ChangelogItem, error)
}

type changelogRepository struct {
	q *db.Queries
}

func NewChangelogRepository(q *db.Queries) ChangelogRepository {
	if q == nil {
		panic("NewChangelogRepository: q is required")
	}
	return &changelogRepository{q: q}
}

func (r *changelogRepository) ListReleases(ctx context.Context, components []string) ([]db.ChangelogRelease, error) {
	// The query checks cardinality, so nil and empty both mean "all" — but nil
	// reaches pg as NULL, where cardinality is NULL rather than 0. Normalise.
	if components == nil {
		components = []string{}
	}
	return r.q.ListChangelogReleases(ctx, components)
}

func (r *changelogRepository) ListItems(ctx context.Context, releaseIDs []uuid.UUID) ([]db.ChangelogItem, error) {
	if len(releaseIDs) == 0 {
		return nil, nil
	}
	return r.q.ListChangelogItemsForReleases(ctx, releaseIDs)
}

func (r *changelogRepository) CreateRelease(ctx context.Context, arg db.CreateChangelogReleaseParams) (db.ChangelogRelease, error) {
	release, err := r.q.CreateChangelogRelease(ctx, arg)
	// UNIQUE (component, version). Republishing a version is the mistake this
	// guards — it has to fail loudly rather than leave a reader with the same
	// release listed twice.
	var pgErr *pgconn.PgError
	if errors.As(err, &pgErr) && pgErr.Code == uniqueViolationCode {
		return db.ChangelogRelease{}, apperr.Duplicate("changelog_release", "version", arg.Component+" "+arg.Version)
	}
	return release, err
}

func (r *changelogRepository) CreateItem(ctx context.Context, arg db.CreateChangelogItemParams) (db.ChangelogItem, error) {
	return r.q.CreateChangelogItem(ctx, arg)
}
