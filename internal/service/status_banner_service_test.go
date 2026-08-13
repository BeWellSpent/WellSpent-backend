package service

import (
	"context"
	"errors"
	"strings"
	"testing"
	"time"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// ── Mock ──────────────────────────────────────────────────────────────────────

type mockStatusBannerRepo struct {
	getActive func(context.Context) (db.StatusBanner, error)
	create    func(context.Context, db.CreateStatusBannerParams) (db.StatusBanner, error)
	list      func(context.Context, int32) ([]db.StatusBanner, error)
	expire    func(context.Context, uuid.UUID) (db.StatusBanner, error)
}

func (m *mockStatusBannerRepo) GetActive(ctx context.Context) (db.StatusBanner, error) {
	if m.getActive != nil {
		return m.getActive(ctx)
	}
	return db.StatusBanner{}, apperr.NotFound("status_banner", "active")
}

func (m *mockStatusBannerRepo) Create(ctx context.Context, arg db.CreateStatusBannerParams) (db.StatusBanner, error) {
	if m.create != nil {
		return m.create(ctx, arg)
	}
	return db.StatusBanner{}, nil
}

func (m *mockStatusBannerRepo) List(ctx context.Context, limit int32) ([]db.StatusBanner, error) {
	if m.list != nil {
		return m.list(ctx, limit)
	}
	return nil, nil
}

func (m *mockStatusBannerRepo) Expire(ctx context.Context, id uuid.UUID) (db.StatusBanner, error) {
	if m.expire != nil {
		return m.expire(ctx, id)
	}
	return db.StatusBanner{}, nil
}

// ── Helpers ───────────────────────────────────────────────────────────────────

// userRepoReturning builds a user repo whose GetByID answers with an account of
// the given privilege, so each test states only the thing it cares about.
func userRepoReturning(id uuid.UUID, superuser bool) *mockUserRepo {
	return &mockUserRepo{
		getByID: func(_ context.Context, got uuid.UUID) (db.User, error) {
			if got != id {
				return db.User{}, apperr.NotFound("user", got.String())
			}
			return db.User{ID: id, IsSuperuser: superuser}, nil
		},
	}
}

func validCreateParams() CreateStatusBannerParams {
	return CreateStatusBannerParams{
		Severity:  "warning",
		MessageEn: "Bank syncing is delayed while we work with our provider.",
		MessageEs: "La sincronización bancaria está retrasada.",
		EndsAt:    time.Now().Add(6 * time.Hour),
	}
}

// ── GetActive ─────────────────────────────────────────────────────────────────

func TestStatusBanner_GetActive_ReturnsLiveBanner(t *testing.T) {
	want := db.StatusBanner{ID: uuid.New(), Severity: "critical", MessageEn: "We're down."}
	svc := NewStatusBannerService(
		&mockStatusBannerRepo{getActive: func(context.Context) (db.StatusBanner, error) { return want, nil }},
		&mockUserRepo{},
	)

	got, found, err := svc.GetActive(context.Background())

	require.NoError(t, err)
	assert.True(t, found)
	assert.Equal(t, want.ID, got.ID)
}

// "Nothing to show" is the normal state — it must not reach the handler as a
// NotFound, or every client would log a 404 on every page load.
func TestStatusBanner_GetActive_NoBannerIsNotAnError(t *testing.T) {
	svc := NewStatusBannerService(
		&mockStatusBannerRepo{getActive: func(context.Context) (db.StatusBanner, error) {
			return db.StatusBanner{}, apperr.NotFound("status_banner", "active")
		}},
		&mockUserRepo{},
	)

	_, found, err := svc.GetActive(context.Background())

	require.NoError(t, err)
	assert.False(t, found)
}

// A genuine failure must still propagate — swallowing everything would hide a
// broken database behind a silent "no banner".
func TestStatusBanner_GetActive_PropagatesRealErrors(t *testing.T) {
	boom := errors.New("connection refused")
	svc := NewStatusBannerService(
		&mockStatusBannerRepo{getActive: func(context.Context) (db.StatusBanner, error) {
			return db.StatusBanner{}, boom
		}},
		&mockUserRepo{},
	)

	_, found, err := svc.GetActive(context.Background())

	require.ErrorIs(t, err, boom)
	assert.False(t, found)
}

// ── Create: authorization ─────────────────────────────────────────────────────

func TestStatusBanner_Create_SuperuserSucceeds(t *testing.T) {
	userID := uuid.New()
	var saved db.CreateStatusBannerParams
	svc := NewStatusBannerService(
		&mockStatusBannerRepo{create: func(_ context.Context, arg db.CreateStatusBannerParams) (db.StatusBanner, error) {
			saved = arg
			return db.StatusBanner{ID: uuid.New(), Severity: arg.Severity}, nil
		}},
		userRepoReturning(userID, true),
	)

	got, err := svc.Create(context.Background(), userID, validCreateParams())

	require.NoError(t, err)
	assert.Equal(t, "warning", got.Severity)
	assert.Equal(t, userID, saved.CreatedBy, "the banner records who posted it")
}

func TestStatusBanner_Create_NonSuperuserForbidden(t *testing.T) {
	userID := uuid.New()
	created := false
	svc := NewStatusBannerService(
		&mockStatusBannerRepo{create: func(context.Context, db.CreateStatusBannerParams) (db.StatusBanner, error) {
			created = true
			return db.StatusBanner{}, nil
		}},
		userRepoReturning(userID, false),
	)

	_, err := svc.Create(context.Background(), userID, validCreateParams())

	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
	assert.False(t, created, "an unprivileged caller must not reach the repository")
}

// ── Create: validation ────────────────────────────────────────────────────────

func TestStatusBanner_Create_RejectsUnknownSeverity(t *testing.T) {
	userID := uuid.New()
	svc := NewStatusBannerService(&mockStatusBannerRepo{}, userRepoReturning(userID, true))

	p := validCreateParams()
	p.Severity = "catastrophic"
	_, err := svc.Create(context.Background(), userID, p)

	var invalid *apperr.ValidationError
	require.ErrorAs(t, err, &invalid)
}

// Typing "Warning" during an incident shouldn't be rejected on a technicality.
func TestStatusBanner_Create_NormalizesSeverityCase(t *testing.T) {
	userID := uuid.New()
	var saved db.CreateStatusBannerParams
	svc := NewStatusBannerService(
		&mockStatusBannerRepo{create: func(_ context.Context, arg db.CreateStatusBannerParams) (db.StatusBanner, error) {
			saved = arg
			return db.StatusBanner{}, nil
		}},
		userRepoReturning(userID, true),
	)

	p := validCreateParams()
	p.Severity = "  CRITICAL "
	_, err := svc.Create(context.Background(), userID, p)

	require.NoError(t, err)
	assert.Equal(t, "critical", saved.Severity)
}

func TestStatusBanner_Create_RejectsEmptyEnglishMessage(t *testing.T) {
	userID := uuid.New()
	svc := NewStatusBannerService(&mockStatusBannerRepo{}, userRepoReturning(userID, true))

	p := validCreateParams()
	p.MessageEn = "   "
	_, err := svc.Create(context.Background(), userID, p)

	var invalid *apperr.ValidationError
	require.ErrorAs(t, err, &invalid)
}

// Spanish is optional — clients fall back to English rather than showing a
// blank bar.
func TestStatusBanner_Create_AllowsEmptySpanishMessage(t *testing.T) {
	userID := uuid.New()
	svc := NewStatusBannerService(&mockStatusBannerRepo{}, userRepoReturning(userID, true))

	p := validCreateParams()
	p.MessageEs = ""
	_, err := svc.Create(context.Background(), userID, p)

	require.NoError(t, err)
}

func TestStatusBanner_Create_RejectsMessageOverLimit(t *testing.T) {
	userID := uuid.New()
	svc := NewStatusBannerService(&mockStatusBannerRepo{}, userRepoReturning(userID, true))

	p := validCreateParams()
	p.MessageEn = strings.Repeat("a", maxBannerMessageLength+1)
	_, err := svc.Create(context.Background(), userID, p)

	var invalid *apperr.ValidationError
	require.ErrorAs(t, err, &invalid)
}

// Counted in runes, not bytes. 300 accented characters are 600 bytes in UTF-8,
// so a byte-based check would reject a Spanish message that is exactly at the
// limit — the language most likely to hit it.
func TestStatusBanner_Create_CountsCharactersNotBytes(t *testing.T) {
	userID := uuid.New()
	svc := NewStatusBannerService(&mockStatusBannerRepo{}, userRepoReturning(userID, true))

	p := validCreateParams()
	p.MessageEs = strings.Repeat("á", maxBannerMessageLength)
	_, err := svc.Create(context.Background(), userID, p)

	require.NoError(t, err, "300 multi-byte characters is exactly at the limit, not over it")
}

func TestStatusBanner_Create_RejectsEndBeforeStart(t *testing.T) {
	userID := uuid.New()
	svc := NewStatusBannerService(&mockStatusBannerRepo{}, userRepoReturning(userID, true))

	start := time.Now().Add(4 * time.Hour)
	p := validCreateParams()
	p.StartsAt = &start
	p.EndsAt = start.Add(-time.Hour)
	_, err := svc.Create(context.Background(), userID, p)

	var invalid *apperr.ValidationError
	require.ErrorAs(t, err, &invalid)
}

// A window that has already closed would write a row nobody ever sees —
// almost always a timezone slip by whoever is typing under pressure.
func TestStatusBanner_Create_RejectsAlreadyExpiredWindow(t *testing.T) {
	userID := uuid.New()
	svc := NewStatusBannerService(&mockStatusBannerRepo{}, userRepoReturning(userID, true))

	start := time.Now().Add(-6 * time.Hour)
	p := validCreateParams()
	p.StartsAt = &start
	p.EndsAt = time.Now().Add(-time.Hour)
	_, err := svc.Create(context.Background(), userID, p)

	var invalid *apperr.ValidationError
	require.ErrorAs(t, err, &invalid)
}

func TestStatusBanner_Create_RequiresEndsAt(t *testing.T) {
	userID := uuid.New()
	svc := NewStatusBannerService(&mockStatusBannerRepo{}, userRepoReturning(userID, true))

	p := validCreateParams()
	p.EndsAt = time.Time{}
	_, err := svc.Create(context.Background(), userID, p)

	var invalid *apperr.ValidationError
	require.ErrorAs(t, err, &invalid)
}

// The "announce this right now" case must need no timestamp arithmetic.
func TestStatusBanner_Create_DefaultsStartToNow(t *testing.T) {
	userID := uuid.New()
	var saved db.CreateStatusBannerParams
	svc := NewStatusBannerService(
		&mockStatusBannerRepo{create: func(_ context.Context, arg db.CreateStatusBannerParams) (db.StatusBanner, error) {
			saved = arg
			return db.StatusBanner{}, nil
		}},
		userRepoReturning(userID, true),
	)

	before := time.Now().Add(-time.Second)
	p := validCreateParams()
	p.StartsAt = nil
	_, err := svc.Create(context.Background(), userID, p)

	require.NoError(t, err)
	require.True(t, saved.StartsAt.Valid)
	assert.WithinRange(t, saved.StartsAt.Time, before, time.Now().Add(time.Second))
}

func TestStatusBanner_Create_HonoursExplicitStart(t *testing.T) {
	userID := uuid.New()
	var saved db.CreateStatusBannerParams
	svc := NewStatusBannerService(
		&mockStatusBannerRepo{create: func(_ context.Context, arg db.CreateStatusBannerParams) (db.StatusBanner, error) {
			saved = arg
			return db.StatusBanner{}, nil
		}},
		userRepoReturning(userID, true),
	)

	start := time.Now().Add(2 * time.Hour)
	p := validCreateParams()
	p.StartsAt = &start
	p.EndsAt = start.Add(time.Hour)
	_, err := svc.Create(context.Background(), userID, p)

	require.NoError(t, err)
	assert.WithinDuration(t, start, saved.StartsAt.Time, time.Second)
}

// ── List / Expire ─────────────────────────────────────────────────────────────

func TestStatusBanner_List_NonSuperuserForbidden(t *testing.T) {
	userID := uuid.New()
	svc := NewStatusBannerService(&mockStatusBannerRepo{}, userRepoReturning(userID, false))

	_, err := svc.List(context.Background(), userID, 10)

	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
}

func TestStatusBanner_List_AppliesDefaultLimit(t *testing.T) {
	userID := uuid.New()
	var gotLimit int32
	svc := NewStatusBannerService(
		&mockStatusBannerRepo{list: func(_ context.Context, limit int32) ([]db.StatusBanner, error) {
			gotLimit = limit
			return []db.StatusBanner{{ID: uuid.New()}}, nil
		}},
		userRepoReturning(userID, true),
	)

	got, err := svc.List(context.Background(), userID, 0)

	require.NoError(t, err)
	assert.Len(t, got, 1)
	assert.Equal(t, int32(defaultBannerListLimit), gotLimit)
}

func TestStatusBanner_Expire_NonSuperuserForbidden(t *testing.T) {
	userID := uuid.New()
	expired := false
	svc := NewStatusBannerService(
		&mockStatusBannerRepo{expire: func(context.Context, uuid.UUID) (db.StatusBanner, error) {
			expired = true
			return db.StatusBanner{}, nil
		}},
		userRepoReturning(userID, false),
	)

	_, err := svc.Expire(context.Background(), userID, uuid.New())

	var forbidden *apperr.ForbiddenError
	require.ErrorAs(t, err, &forbidden)
	assert.False(t, expired)
}

func TestStatusBanner_Expire_SuperuserSucceeds(t *testing.T) {
	userID := uuid.New()
	bannerID := uuid.New()
	svc := NewStatusBannerService(
		&mockStatusBannerRepo{expire: func(_ context.Context, id uuid.UUID) (db.StatusBanner, error) {
			return db.StatusBanner{
				ID:     id,
				EndsAt: pgtype.Timestamptz{Time: time.Now(), Valid: true},
			}, nil
		}},
		userRepoReturning(userID, true),
	)

	got, err := svc.Expire(context.Background(), userID, bannerID)

	require.NoError(t, err)
	assert.Equal(t, bannerID, got.ID)
}

// A deleted or mistyped ID must surface as NotFound rather than a silent no-op.
func TestStatusBanner_Expire_UnknownIDNotFound(t *testing.T) {
	userID := uuid.New()
	svc := NewStatusBannerService(
		&mockStatusBannerRepo{expire: func(_ context.Context, id uuid.UUID) (db.StatusBanner, error) {
			return db.StatusBanner{}, apperr.NotFound("status_banner", id.String())
		}},
		userRepoReturning(userID, true),
	)

	_, err := svc.Expire(context.Background(), userID, uuid.New())

	var notFound *apperr.NotFoundError
	require.ErrorAs(t, err, &notFound)
}
