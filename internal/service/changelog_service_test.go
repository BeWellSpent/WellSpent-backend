package service

import (
	"context"
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

type mockChangelogRepo struct {
	listReleases  func(context.Context, []string) ([]db.ChangelogRelease, error)
	listItems     func(context.Context, []uuid.UUID) ([]db.ChangelogItem, error)
	createRelease func(context.Context, db.CreateChangelogReleaseParams) (db.ChangelogRelease, error)
	createItem    func(context.Context, db.CreateChangelogItemParams) (db.ChangelogItem, error)
}

func (m *mockChangelogRepo) ListReleases(ctx context.Context, components []string) ([]db.ChangelogRelease, error) {
	if m.listReleases != nil {
		return m.listReleases(ctx, components)
	}
	return nil, nil
}

func (m *mockChangelogRepo) ListItems(ctx context.Context, ids []uuid.UUID) ([]db.ChangelogItem, error) {
	if m.listItems != nil {
		return m.listItems(ctx, ids)
	}
	return nil, nil
}

func (m *mockChangelogRepo) CreateRelease(ctx context.Context, arg db.CreateChangelogReleaseParams) (db.ChangelogRelease, error) {
	if m.createRelease != nil {
		return m.createRelease(ctx, arg)
	}
	return db.ChangelogRelease{ID: uuid.New(), Component: arg.Component, Version: arg.Version, ReleasedAt: arg.ReleasedAt}, nil
}

func (m *mockChangelogRepo) CreateItem(ctx context.Context, arg db.CreateChangelogItemParams) (db.ChangelogItem, error) {
	if m.createItem != nil {
		return m.createItem(ctx, arg)
	}
	return db.ChangelogItem{
		ID: uuid.New(), ReleaseID: arg.ReleaseID, ChangeType: arg.ChangeType,
		SummaryEn: arg.SummaryEn, SummaryEs: arg.SummaryEs, Position: arg.Position,
	}, nil
}

func release(component, version string, daysAgo int) db.ChangelogRelease {
	return db.ChangelogRelease{
		ID:         uuid.New(),
		Component:  component,
		Version:    version,
		ReleasedAt: pgtype.Timestamptz{Time: time.Now().AddDate(0, 0, -daysAgo), Valid: true},
	}
}

func validItems() []CreateItemParams {
	return []CreateItemParams{{ChangeType: ChangeTypeAdded, SummaryEn: "Something new"}}
}

// ── ListReleases ──────────────────────────────────────────────────────────────

func TestListReleases_AttachesItemsToTheirRelease(t *testing.T) {
	web := release(ComponentWeb, "1.27.0", 1)
	server := release(ComponentServer, "1.0.0", 2)

	svc := NewChangelogService(&mockChangelogRepo{
		listReleases: func(_ context.Context, _ []string) ([]db.ChangelogRelease, error) {
			return []db.ChangelogRelease{web, server}, nil
		},
		listItems: func(_ context.Context, _ []uuid.UUID) ([]db.ChangelogItem, error) {
			return []db.ChangelogItem{
				{ReleaseID: web.ID, ChangeType: ChangeTypeAdded, SummaryEn: "web thing"},
				{ReleaseID: server.ID, ChangeType: ChangeTypeFixed, SummaryEn: "server thing"},
				{ReleaseID: web.ID, ChangeType: ChangeTypeFixed, SummaryEn: "another web thing"},
			}, nil
		},
	}, userRepoReturning(uuid.New(), false))

	out, err := svc.ListReleases(context.Background(), nil, 0)
	require.NoError(t, err)
	require.Len(t, out, 2)
	assert.Len(t, out[0].Items, 2, "both web items land on the web release")
	assert.Len(t, out[1].Items, 1)
	assert.Equal(t, "server thing", out[1].Items[0].SummaryEn)
}

// The cap is per component, not overall — a client asking for its own client
// plus the server must not have one component's history crowd out the other's.
func TestListReleases_LimitIsPerComponentNotOverall(t *testing.T) {
	svc := NewChangelogService(&mockChangelogRepo{
		listReleases: func(_ context.Context, _ []string) ([]db.ChangelogRelease, error) {
			return []db.ChangelogRelease{
				release(ComponentWeb, "1.3.0", 1),
				release(ComponentWeb, "1.2.0", 2),
				release(ComponentWeb, "1.1.0", 3),
				release(ComponentServer, "1.1.0", 1),
				release(ComponentServer, "1.0.0", 2),
			}, nil
		},
	}, userRepoReturning(uuid.New(), false))

	out, err := svc.ListReleases(context.Background(), nil, 2)
	require.NoError(t, err)
	require.Len(t, out, 4)

	perComponent := map[string]int{}
	for _, r := range out {
		perComponent[r.Component]++
	}
	assert.Equal(t, 2, perComponent[ComponentWeb])
	assert.Equal(t, 2, perComponent[ComponentServer])
}

func TestListReleases_RejectsUnknownComponent(t *testing.T) {
	svc := NewChangelogService(&mockChangelogRepo{}, userRepoReturning(uuid.New(), false))
	_, err := svc.ListReleases(context.Background(), []string{"android"}, 0)
	assert.ErrorAs(t, err, new(*apperr.ValidationError))
}

func TestListReleases_NoReleasesIsNotAnError(t *testing.T) {
	svc := NewChangelogService(&mockChangelogRepo{}, userRepoReturning(uuid.New(), false))
	out, err := svc.ListReleases(context.Background(), nil, 0)
	require.NoError(t, err)
	assert.Empty(t, out)
}

// ── CreateRelease ─────────────────────────────────────────────────────────────

func TestCreateRelease_ForbiddenForNonSuperuser(t *testing.T) {
	userID := uuid.New()
	svc := NewChangelogService(&mockChangelogRepo{}, userRepoReturning(userID, false))

	_, err := svc.CreateRelease(context.Background(), userID, CreateReleaseParams{
		Component: ComponentWeb, Version: "1.27.0", Items: validItems(),
	})
	assert.ErrorAs(t, err, new(*apperr.ForbiddenError))
}

func TestCreateRelease_SuperuserPublishesItemsInOrder(t *testing.T) {
	userID := uuid.New()
	svc := NewChangelogService(&mockChangelogRepo{}, userRepoReturning(userID, true))

	out, err := svc.CreateRelease(context.Background(), userID, CreateReleaseParams{
		Component: ComponentIOS,
		Version:   "1.36.0",
		Items: []CreateItemParams{
			{ChangeType: ChangeTypeAdded, SummaryEn: "first", SummaryEs: "primero"},
			{ChangeType: ChangeTypeFixed, SummaryEn: "second"},
		},
	})
	require.NoError(t, err)
	require.Len(t, out.Items, 2)
	// Position preserves the order the operator wrote them in; grouping by
	// change type is left to the clients.
	assert.Equal(t, int32(0), out.Items[0].Position)
	assert.Equal(t, int32(1), out.Items[1].Position)
	assert.Equal(t, "primero", out.Items[0].SummaryEs)
	assert.Equal(t, "", out.Items[1].SummaryEs, "Spanish is optional; clients fall back to English")
}

// A release with nothing to say would render as an empty "what's new", which
// is worse than showing nothing at all.
func TestCreateRelease_RejectsAReleaseWithNoItems(t *testing.T) {
	userID := uuid.New()
	svc := NewChangelogService(&mockChangelogRepo{}, userRepoReturning(userID, true))

	_, err := svc.CreateRelease(context.Background(), userID, CreateReleaseParams{
		Component: ComponentWeb, Version: "1.27.0",
	})
	assert.ErrorAs(t, err, new(*apperr.ValidationError))
}

func TestCreateRelease_RejectsInvalidInput(t *testing.T) {
	userID := uuid.New()
	svc := NewChangelogService(&mockChangelogRepo{}, userRepoReturning(userID, true))

	cases := map[string]CreateReleaseParams{
		"unknown component":   {Component: "android", Version: "1.0.0", Items: validItems()},
		"blank version":       {Component: ComponentWeb, Version: "   ", Items: validItems()},
		"unknown change type": {Component: ComponentWeb, Version: "1.0.0", Items: []CreateItemParams{{ChangeType: "removed", SummaryEn: "x"}}},
		"blank English":       {Component: ComponentWeb, Version: "1.0.0", Items: []CreateItemParams{{ChangeType: ChangeTypeAdded, SummaryEn: "  "}}},
	}
	for name, params := range cases {
		t.Run(name, func(t *testing.T) {
			_, err := svc.CreateRelease(context.Background(), userID, params)
			assert.ErrorAs(t, err, new(*apperr.ValidationError))
		})
	}
}

// Counted in runes, not bytes: 300 accented characters are 600 bytes in UTF-8,
// so a byte cap would cut a Spanish summary short before an English one of the
// same visible length.
func TestCreateRelease_SummaryCapCountsRunesNotBytes(t *testing.T) {
	userID := uuid.New()
	svc := NewChangelogService(&mockChangelogRepo{}, userRepoReturning(userID, true))

	exactly300Accented := ""
	for i := 0; i < maxChangelogSummaryLength; i++ {
		exactly300Accented += "á"
	}

	_, err := svc.CreateRelease(context.Background(), userID, CreateReleaseParams{
		Component: ComponentWeb, Version: "1.0.0",
		Items: []CreateItemParams{{ChangeType: ChangeTypeAdded, SummaryEn: "ok", SummaryEs: exactly300Accented}},
	})
	require.NoError(t, err, "300 accented characters is 600 bytes but only 300 runes")

	_, err = svc.CreateRelease(context.Background(), userID, CreateReleaseParams{
		Component: ComponentWeb, Version: "1.0.1",
		Items: []CreateItemParams{{ChangeType: ChangeTypeAdded, SummaryEn: exactly300Accented + "á"}},
	})
	assert.ErrorAs(t, err, new(*apperr.ValidationError))
}

func TestCreateRelease_DefaultsReleasedAtToNow(t *testing.T) {
	userID := uuid.New()
	var captured db.CreateChangelogReleaseParams
	svc := NewChangelogService(&mockChangelogRepo{
		createRelease: func(_ context.Context, arg db.CreateChangelogReleaseParams) (db.ChangelogRelease, error) {
			captured = arg
			return db.ChangelogRelease{ID: uuid.New(), Component: arg.Component, Version: arg.Version}, nil
		},
	}, userRepoReturning(userID, true))

	_, err := svc.CreateRelease(context.Background(), userID, CreateReleaseParams{
		Component: ComponentServer, Version: "1.0.0", Items: validItems(),
	})
	require.NoError(t, err)
	assert.True(t, captured.ReleasedAt.Valid)
	assert.WithinDuration(t, time.Now().UTC(), captured.ReleasedAt.Time, time.Minute)
}

func TestServerVersion_IsReported(t *testing.T) {
	svc := NewChangelogService(&mockChangelogRepo{}, userRepoReturning(uuid.New(), false))
	// A client cannot tell which server releases are new to it without this.
	assert.NotEmpty(t, svc.ServerVersion())
}
