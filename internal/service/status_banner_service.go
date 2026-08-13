package service

import (
	"context"
	"errors"
	"strings"
	"time"
	"unicode/utf8"

	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
)

// maxBannerMessageLength is the per-language cap from the feature spec. The DB
// enforces the same limit; this exists so the caller gets a clear validation
// error instead of a constraint violation surfacing as a 500.
const maxBannerMessageLength = 300

// defaultBannerListLimit caps ListStatusBanners when the caller doesn't ask for
// a specific number.
const defaultBannerListLimit = 50

// severity values as stored. Kept as strings rather than an enum type so what
// is in the column is readable in psql during an incident, which is the one
// moment someone will be reading it by hand.
const (
	bannerSeverityInfo     = "info"
	bannerSeverityWarning  = "warning"
	bannerSeverityCritical = "critical"
)

var validBannerSeverities = map[string]bool{
	bannerSeverityInfo:     true,
	bannerSeverityWarning:  true,
	bannerSeverityCritical: true,
}

type StatusBannerService struct {
	banners repository.StatusBannerRepository
	users   repository.UserRepository
}

func NewStatusBannerService(
	banners repository.StatusBannerRepository,
	users repository.UserRepository,
) *StatusBannerService {
	if banners == nil {
		panic("NewStatusBannerService: banners is required")
	}
	if users == nil {
		panic("NewStatusBannerService: users is required")
	}
	return &StatusBannerService{banners: banners, users: users}
}

// CreateStatusBannerParams is the service-level input for Create. StartsAt is
// optional — nil means "now", which is the case that matters when something is
// on fire.
type CreateStatusBannerParams struct {
	Severity  string
	MessageEn string
	MessageEs string
	StartsAt  *time.Time
	EndsAt    time.Time
}

// GetActive returns the banner clients should show. The bool is false when
// nothing is live, which is the normal state — callers must not treat it as an
// error, and this deliberately does not surface the repository's NotFound to
// the handler, where it would become a 404 on a perfectly healthy request.
func (s *StatusBannerService) GetActive(ctx context.Context) (db.StatusBanner, bool, error) {
	banner, err := s.banners.GetActive(ctx)
	if err != nil {
		var notFound *apperr.NotFoundError
		if errors.As(err, &notFound) {
			return db.StatusBanner{}, false, nil
		}
		return db.StatusBanner{}, false, err
	}
	return banner, true, nil
}

// assertSuperuser is the gate on every write below. This is the first code in
// the product to read is_superuser — the column has existed since migration
// 000001 but nothing has ever consulted it.
func (s *StatusBannerService) assertSuperuser(ctx context.Context, userID uuid.UUID) error {
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

func (s *StatusBannerService) Create(ctx context.Context, userID uuid.UUID, p CreateStatusBannerParams) (db.StatusBanner, error) {
	if err := s.assertSuperuser(ctx, userID); err != nil {
		return db.StatusBanner{}, err
	}

	severity := strings.TrimSpace(strings.ToLower(p.Severity))
	if !validBannerSeverities[severity] {
		return db.StatusBanner{}, apperr.Invalid("severity must be one of: info, warning, critical")
	}

	messageEn := strings.TrimSpace(p.MessageEn)
	messageEs := strings.TrimSpace(p.MessageEs)
	if messageEn == "" {
		return db.StatusBanner{}, apperr.Invalid("message_en is required")
	}
	// Counted in runes, not bytes: an accented Spanish message would otherwise
	// hit the limit earlier than an English one of the same visible length.
	if utf8.RuneCountInString(messageEn) > maxBannerMessageLength {
		return db.StatusBanner{}, apperr.Invalid("message_en must be 300 characters or fewer")
	}
	if utf8.RuneCountInString(messageEs) > maxBannerMessageLength {
		return db.StatusBanner{}, apperr.Invalid("message_es must be 300 characters or fewer")
	}

	startsAt := time.Now().UTC()
	if p.StartsAt != nil && !p.StartsAt.IsZero() {
		startsAt = p.StartsAt.UTC()
	}
	if p.EndsAt.IsZero() {
		return db.StatusBanner{}, apperr.Invalid("ends_at is required")
	}
	endsAt := p.EndsAt.UTC()
	if !endsAt.After(startsAt) {
		return db.StatusBanner{}, apperr.Invalid("ends_at must be after starts_at")
	}
	// A banner whose window has already closed would be written and never seen.
	// Almost certainly a timezone mistake by whoever is typing this under
	// pressure, so it's rejected rather than silently accepted.
	if !endsAt.After(time.Now().UTC()) {
		return db.StatusBanner{}, apperr.Invalid("ends_at must be in the future")
	}

	return s.banners.Create(ctx, db.CreateStatusBannerParams{
		Severity:  severity,
		MessageEn: messageEn,
		MessageEs: messageEs,
		StartsAt:  pgtype.Timestamptz{Time: startsAt, Valid: true},
		EndsAt:    pgtype.Timestamptz{Time: endsAt, Valid: true},
		CreatedBy: userID,
	})
}

func (s *StatusBannerService) List(ctx context.Context, userID uuid.UUID, limit int32) ([]db.StatusBanner, error) {
	if err := s.assertSuperuser(ctx, userID); err != nil {
		return nil, err
	}
	if limit <= 0 {
		limit = defaultBannerListLimit
	}
	return s.banners.List(ctx, limit)
}

func (s *StatusBannerService) Expire(ctx context.Context, userID, bannerID uuid.UUID) (db.StatusBanner, error) {
	if err := s.assertSuperuser(ctx, userID); err != nil {
		return db.StatusBanner{}, err
	}
	return s.banners.Expire(ctx, bannerID)
}
