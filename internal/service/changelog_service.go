package service

import (
	"context"
	"strings"
	"time"
	"unicode/utf8"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/BeWellSpent/wellspent-backend/internal/version"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
)

// Component and change-type values as stored. Strings rather than enum types
// for the same reason status_banner stores its severity that way: what is in
// the column stays readable in psql.
const (
	ComponentWeb    = "web"
	ComponentIOS    = "ios"
	ComponentServer = "server"

	ChangeTypeAdded   = "added"
	ChangeTypeFixed   = "fixed"
	ChangeTypeChanged = "changed"
)

var validComponents = map[string]bool{
	ComponentWeb:    true,
	ComponentIOS:    true,
	ComponentServer: true,
}

var validChangeTypes = map[string]bool{
	ChangeTypeAdded:   true,
	ChangeTypeFixed:   true,
	ChangeTypeChanged: true,
}

// Caps mirroring the DB constraints, so a caller gets a validation error
// rather than a constraint violation surfacing as a 500. Counted in runes:
// 300 accented characters are 600 bytes in UTF-8, so a byte cap would cut a
// Spanish summary short before an English one of the same visible length.
const (
	maxChangelogSummaryLength = 300
	maxChangelogVersionLength = 40
)

// defaultReleasesPerComponent caps how much history a caller gets when it
// doesn't ask for a specific number.
const defaultReleasesPerComponent = 20

type ChangelogService struct {
	changelog repository.ChangelogRepository
	users     repository.UserRepository
}

func NewChangelogService(changelog repository.ChangelogRepository, users repository.UserRepository) *ChangelogService {
	if changelog == nil {
		panic("NewChangelogService: changelog is required")
	}
	if users == nil {
		panic("NewChangelogService: users is required")
	}
	return &ChangelogService{changelog: changelog, users: users}
}

// Release is a changelog release with its items attached, which is the only
// shape a caller ever wants — a release with no items would render as an empty
// "what's new".
type Release struct {
	db.ChangelogRelease
	Items []db.ChangelogItem
}

// CreateReleaseParams is the service-level input for CreateRelease.
type CreateReleaseParams struct {
	Component  string
	Version    string
	ReleasedAt *pgtype.Timestamptz // nil means now
	Items      []CreateItemParams
}

type CreateItemParams struct {
	ChangeType string
	SummaryEn  string
	SummaryEs  string
}

// ServerVersion is what this build calls itself. Exposed through the service so
// handlers don't reach into the version package directly and so a test can
// reason about the value the RPC actually returns.
func (s *ChangelogService) ServerVersion() string {
	return version.Current
}

// ListReleases returns releases for the given components, newest first,
// truncated to limitPerComponent each. An empty components slice means all.
//
// Not superuser-gated: this is the reader-facing call, serving both the
// "what's new" prompt and the Help browser.
func (s *ChangelogService) ListReleases(ctx context.Context, components []string, limitPerComponent int32) ([]Release, error) {
	for _, c := range components {
		if !validComponents[c] {
			return nil, apperr.Invalid("unknown component: " + c)
		}
	}
	if limitPerComponent <= 0 {
		limitPerComponent = defaultReleasesPerComponent
	}

	releases, err := s.changelog.ListReleases(ctx, components)
	if err != nil {
		return nil, err
	}

	// The query orders by component then released_at DESC, so truncating is a
	// running count per component rather than a sort. Doing it here instead of
	// in SQL avoids a window function for what is a display cap.
	perComponent := map[string]int32{}
	kept := make([]db.ChangelogRelease, 0, len(releases))
	for _, r := range releases {
		if perComponent[r.Component] >= limitPerComponent {
			continue
		}
		perComponent[r.Component]++
		kept = append(kept, r)
	}

	ids := make([]uuid.UUID, 0, len(kept))
	for _, r := range kept {
		ids = append(ids, r.ID)
	}
	items, err := s.changelog.ListItems(ctx, ids)
	if err != nil {
		return nil, err
	}
	itemsByRelease := map[uuid.UUID][]db.ChangelogItem{}
	for _, item := range items {
		itemsByRelease[item.ReleaseID] = append(itemsByRelease[item.ReleaseID], item)
	}

	out := make([]Release, 0, len(kept))
	for _, r := range kept {
		out = append(out, Release{ChangelogRelease: r, Items: itemsByRelease[r.ID]})
	}
	return out, nil
}

// CreateRelease publishes one release and all its items. Superuser only.
//
// There is no transaction wrapper, matching the rest of this codebase's
// multi-write paths. A partial failure leaves a release with fewer items than
// intended rather than a phantom release: items are written in order, and the
// release row is the thing clients key on, so re-running the publish for the
// same version fails the unique constraint loudly instead of silently
// duplicating.
func (s *ChangelogService) CreateRelease(ctx context.Context, userID uuid.UUID, p CreateReleaseParams) (Release, error) {
	if err := s.assertSuperuser(ctx, userID); err != nil {
		return Release{}, err
	}
	if !validComponents[p.Component] {
		return Release{}, apperr.Invalid("must be one of web, ios, server")
	}

	p.Version = strings.TrimSpace(p.Version)
	if p.Version == "" {
		return Release{}, apperr.Invalid("version is required")
	}
	if utf8.RuneCountInString(p.Version) > maxChangelogVersionLength {
		return Release{}, apperr.Invalid("version is too long")
	}

	// A release with nothing to say should not be published — it would show a
	// reader an empty "what's new", which is worse than showing nothing.
	if len(p.Items) == 0 {
		return Release{}, apperr.Invalid("at least one item is required")
	}
	for i := range p.Items {
		p.Items[i].SummaryEn = strings.TrimSpace(p.Items[i].SummaryEn)
		p.Items[i].SummaryEs = strings.TrimSpace(p.Items[i].SummaryEs)
		if !validChangeTypes[p.Items[i].ChangeType] {
			return Release{}, apperr.Invalid("must be one of added, fixed, changed")
		}
		if p.Items[i].SummaryEn == "" {
			return Release{}, apperr.Invalid("English summary is required")
		}
		if utf8.RuneCountInString(p.Items[i].SummaryEn) > maxChangelogSummaryLength ||
			utf8.RuneCountInString(p.Items[i].SummaryEs) > maxChangelogSummaryLength {
			return Release{}, apperr.Invalid("summary is too long")
		}
	}

	releasedAt := pgtype.Timestamptz{Time: time.Now().UTC(), Valid: true}
	if p.ReleasedAt != nil && p.ReleasedAt.Valid {
		releasedAt = *p.ReleasedAt
	}

	release, err := s.changelog.CreateRelease(ctx, db.CreateChangelogReleaseParams{
		Component:  p.Component,
		Version:    p.Version,
		ReleasedAt: releasedAt,
		CreatedBy:  userID,
	})
	if err != nil {
		// The repository maps the unique violation to a Duplicate error —
		// republishing a version has to fail loudly, not list it twice.
		return Release{}, err
	}

	items := make([]db.ChangelogItem, 0, len(p.Items))
	for i, it := range p.Items {
		created, err := s.changelog.CreateItem(ctx, db.CreateChangelogItemParams{
			ReleaseID:  release.ID,
			ChangeType: it.ChangeType,
			SummaryEn:  it.SummaryEn,
			SummaryEs:  it.SummaryEs,
			Position:   int32(i),
		})
		if err != nil {
			return Release{}, err
		}
		items = append(items, created)
	}
	return Release{ChangelogRelease: release, Items: items}, nil
}

// assertSuperuser gates the write path, mirroring StatusBannerService's.
func (s *ChangelogService) assertSuperuser(ctx context.Context, userID uuid.UUID) error {
	user, err := s.users.GetByID(ctx, userID)
	if err != nil {
		return err
	}
	if !user.IsSuperuser {
		// Same message regardless of why, so this can't be used to probe which
		// accounts are privileged.
		return apperr.Forbidden("access denied")
	}
	return nil
}
