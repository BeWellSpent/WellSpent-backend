package rest

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	restgen "github.com/BeWellSpent/wellspent-backend/gen/rest"
	"github.com/BeWellSpent/wellspent-backend/internal/apperr"
	"github.com/BeWellSpent/wellspent-backend/internal/auth"
	"github.com/BeWellSpent/wellspent-backend/internal/config"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"go.uber.org/zap"
)

// ─── mocks ───────────────────────────────────────────────────────────────────
//
// Each embeds its repository interface and overrides only the methods the
// endpoint under test reaches. An un-overridden call nil-panics, which is the
// failure we want: a handler that started touching a new repository method
// should not silently pass on a zero value.

type mockUserRepo struct {
	repository.UserRepository
	countries    []db.ListEnabledCountriesRow
	features     []db.CountryFeature
	countriesErr error
}

func (m *mockUserRepo) ListEnabledCountries(context.Context) ([]db.ListEnabledCountriesRow, error) {
	return m.countries, m.countriesErr
}
func (m *mockUserRepo) ListCountryFeatures(context.Context) ([]db.CountryFeature, error) {
	return m.features, nil
}

type mockBannerRepo struct {
	repository.StatusBannerRepository
	banner db.StatusBanner
	err    error
}

func (m *mockBannerRepo) GetActive(context.Context) (db.StatusBanner, error) {
	return m.banner, m.err
}

type mockChangelogRepo struct {
	repository.ChangelogRepository
	releases []db.ChangelogRelease
	items    []db.ChangelogItem
	err      error
}

func (m *mockChangelogRepo) ListReleases(context.Context, []string) ([]db.ChangelogRelease, error) {
	return m.releases, m.err
}
func (m *mockChangelogRepo) ListItems(context.Context, []uuid.UUID) ([]db.ChangelogItem, error) {
	return m.items, nil
}

// ─── harness ─────────────────────────────────────────────────────────────────

const testSecret = "test-secret-value-for-signing-only"

func ts(t time.Time) pgtype.Timestamptz { return pgtype.Timestamptz{Time: t, Valid: true} }

type harness struct {
	mux    *http.ServeMux
	users  *mockUserRepo
	banner *mockBannerRepo
	clog   *mockChangelogRepo
	jwt    *auth.JWTService
}

func newHarness(t *testing.T) *harness {
	t.Helper()
	users := &mockUserRepo{}
	banner := &mockBannerRepo{err: apperr.NotFound("status_banner", "active")}
	clog := &mockChangelogRepo{}
	jwtSvc := auth.NewJWTService(testSecret)
	log := zap.NewNop()

	mux := http.NewServeMux()
	Register(mux, Deps{
		Users:         service.NewUserService(users, nil, "", &config.Config{}, log),
		StatusBanners: service.NewStatusBannerService(banner, users),
		Changelog:     service.NewChangelogService(clog, users),
		JWT:           jwtSvc,
		Logger:        log,
		ServerVersion: "9.9.9",
	})
	return &harness{mux: mux, users: users, banner: banner, clog: clog, jwt: jwtSvc}
}

func (h *harness) get(t *testing.T, path string, headers map[string]string) *httptest.ResponseRecorder {
	t.Helper()
	req := httptest.NewRequest(http.MethodGet, path, nil)
	for k, v := range headers {
		req.Header.Set(k, v)
	}
	rec := httptest.NewRecorder()
	h.mux.ServeHTTP(rec, req)
	return rec
}

func (h *harness) token(t *testing.T) string {
	t.Helper()
	tok, err := h.jwt.GenerateTokenWithLifetime(uuid.New(), time.Hour)
	if err != nil {
		t.Fatalf("generate token: %v", err)
	}
	return "Bearer " + tok
}

func decode[T any](t *testing.T, rec *httptest.ResponseRecorder) T {
	t.Helper()
	var out T
	if err := json.Unmarshal(rec.Body.Bytes(), &out); err != nil {
		t.Fatalf("decode %T: %v (body: %s)", out, err, rec.Body.String())
	}
	return out
}

// ─── ping ────────────────────────────────────────────────────────────────────

func TestPing_ReportsServerVersionAndIsNotCached(t *testing.T) {
	h := newHarness(t)
	rec := h.get(t, "/rest/v1/ping", nil)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200", rec.Code)
	}
	body := decode[restgen.PingResponse](t, rec)
	if body.ServerVersion != "9.9.9" {
		t.Errorf("serverVersion = %q, want 9.9.9", body.ServerVersion)
	}
	// Caching a liveness probe defeats it.
	if got := rec.Header().Get("Cache-Control"); got != "no-store" {
		t.Errorf("Cache-Control = %q, want no-store", got)
	}
}

// ─── countries ───────────────────────────────────────────────────────────────

func TestListCountries_ServesRowsWithFeaturesAndCachesForADay(t *testing.T) {
	h := newHarness(t)
	h.users.countries = []db.ListEnabledCountriesRow{{Code: "US", Name: "United States", IsEnabled: true}}
	h.users.features = []db.CountryFeature{{CountryCode: "US", FeatureName: "before_tax_income", IsEnabled: true}}

	rec := h.get(t, "/rest/v1/countries", nil)
	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200", rec.Code)
	}
	body := decode[restgen.CountriesResponse](t, rec)
	if len(body.Countries) != 1 || body.Countries[0].Code != "US" {
		t.Fatalf("countries = %+v", body.Countries)
	}
	if len(body.Countries[0].Features) != 1 || body.Countries[0].Features[0].Name != "before_tax_income" {
		t.Errorf("features = %+v, want the US flag merged in", body.Countries[0].Features)
	}
	if got := rec.Header().Get("Cache-Control"); got != cacheCountries {
		t.Errorf("Cache-Control = %q, want %q", got, cacheCountries)
	}
	if rec.Header().Get("ETag") == "" {
		t.Error("ETag is empty; conditional requests cannot work without it")
	}
}

func TestListCountries_MatchingIfNoneMatchReturns304WithNoBody(t *testing.T) {
	h := newHarness(t)
	h.users.countries = []db.ListEnabledCountriesRow{{Code: "AR", Name: "Argentina", IsEnabled: true}}

	first := h.get(t, "/rest/v1/countries", nil)
	etag := first.Header().Get("ETag")

	second := h.get(t, "/rest/v1/countries", map[string]string{"If-None-Match": etag})
	if second.Code != http.StatusNotModified {
		t.Fatalf("status = %d, want 304", second.Code)
	}
	if second.Body.Len() != 0 {
		t.Errorf("304 carried a %d-byte body; it must be empty", second.Body.Len())
	}
	// RFC 9110: a 304 must repeat the caching headers a 200 would have sent,
	// or revalidating silently downgrades the client's cache policy.
	if got := second.Header().Get("Cache-Control"); got != cacheCountries {
		t.Errorf("304 Cache-Control = %q, want %q", got, cacheCountries)
	}
}

func TestCountriesETag_ChangesWhenAFeatureFlagFlips(t *testing.T) {
	// The bug this guards: an ETag keyed only on country codes would serve a
	// stale registration form for a full day after a feature flag changed.
	on := restgen.CountriesResponse{Countries: []restgen.Country{{
		Code: "US", Name: "United States", IsEnabled: true,
		Features: []restgen.CountryFeature{{Name: "before_tax_income", IsEnabled: true}},
	}}}
	off := restgen.CountriesResponse{Countries: []restgen.Country{{
		Code: "US", Name: "United States", IsEnabled: true,
		Features: []restgen.CountryFeature{{Name: "before_tax_income", IsEnabled: false}},
	}}}

	if countriesETag(on) == countriesETag(off) {
		t.Error("ETag ignored a feature-flag change")
	}
}

func TestListCountries_ServiceFailureIsA500ThatIsNeverCached(t *testing.T) {
	h := newHarness(t)
	h.users.countriesErr = context.DeadlineExceeded

	rec := h.get(t, "/rest/v1/countries", nil)
	if rec.Code != http.StatusInternalServerError {
		t.Fatalf("status = %d, want 500", rec.Code)
	}
	body := decode[restgen.Error](t, rec)
	if body.Code != string(codeInternal) {
		t.Errorf("code = %q, want %q", body.Code, codeInternal)
	}
	// Without this a shared cache could hold a 500 for the endpoint's max-age.
	if got := rec.Header().Get("Cache-Control"); got != "no-store" {
		t.Errorf("error Cache-Control = %q, want no-store", got)
	}
}

// ─── status banner ───────────────────────────────────────────────────────────

func TestGetActiveStatusBanner_NoLiveBannerIs200WithAStableETag(t *testing.T) {
	h := newHarness(t) // harness defaults to "nothing live"

	rec := h.get(t, "/rest/v1/status/banner", nil)
	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200 (empty is not a 404 here)", rec.Code)
	}
	body := decode[restgen.StatusBannerResponse](t, rec)
	if body.Banner != nil {
		t.Errorf("banner = %+v, want absent", body.Banner)
	}
	// The quiet case is the common one, so it must revalidate cheaply rather
	// than returning an unconditional 200 on every page load.
	if rec.Header().Get("ETag") == "" {
		t.Error("the empty response has no ETag")
	}

	second := h.get(t, "/rest/v1/status/banner", map[string]string{"If-None-Match": rec.Header().Get("ETag")})
	if second.Code != http.StatusNotModified {
		t.Errorf("status = %d, want 304 on the quiet path", second.Code)
	}
}

func TestGetActiveStatusBanner_LiveBannerIsServedAndDiffersFromTheEmptyETag(t *testing.T) {
	h := newHarness(t)
	empty := h.get(t, "/rest/v1/status/banner", nil).Header().Get("ETag")

	now := time.Date(2026, 8, 26, 12, 0, 0, 0, time.UTC)
	h.banner.err = nil
	h.banner.banner = db.StatusBanner{
		ID: uuid.New(), Severity: "critical",
		MessageEn: "Sync is degraded", MessageEs: "La sincronización está degradada",
		StartsAt: ts(now), EndsAt: ts(now.Add(time.Hour)), CreatedAt: ts(now),
	}

	rec := h.get(t, "/rest/v1/status/banner", nil)
	body := decode[restgen.StatusBannerResponse](t, rec)
	if body.Banner == nil {
		t.Fatal("banner absent")
	}
	if body.Banner.Severity != "critical" {
		t.Errorf("severity = %q, want critical (the text column maps straight through)", body.Banner.Severity)
	}
	if body.Banner.MessageEs != "La sincronización está degradada" {
		t.Errorf("messageEs = %q", body.Banner.MessageEs)
	}
	if got := rec.Header().Get("ETag"); got == empty {
		t.Error("a live banner shares the empty response's ETag; a client holding the quiet version would never see it")
	}
	if got := rec.Header().Get("Cache-Control"); got != cacheStatusBanner {
		t.Errorf("Cache-Control = %q, want %q", got, cacheStatusBanner)
	}
}

func TestGetActiveStatusBanner_ETagChangesWhenTheBannerIsExpiredEarly(t *testing.T) {
	// ExpireStatusBanner takes a banner down by moving ends_at. There is no
	// updated_at column, so an ETag keyed on created_at would keep serving a
	// retracted notice for the whole max-age.
	h := newHarness(t)
	now := time.Date(2026, 8, 26, 12, 0, 0, 0, time.UTC)
	id := uuid.New()
	h.banner.err = nil
	h.banner.banner = db.StatusBanner{ID: id, Severity: "info", StartsAt: ts(now), EndsAt: ts(now.Add(time.Hour)), CreatedAt: ts(now)}
	before := h.get(t, "/rest/v1/status/banner", nil).Header().Get("ETag")

	h.banner.banner.EndsAt = ts(now) // expired early; created_at unchanged
	after := h.get(t, "/rest/v1/status/banner", nil).Header().Get("ETag")

	if before == after {
		t.Error("ETag unchanged after an early expiry")
	}
}

// ─── changelog ───────────────────────────────────────────────────────────────

func TestListChangelog_RequiresABearerToken(t *testing.T) {
	h := newHarness(t)

	rec := h.get(t, "/rest/v1/changelog", nil)
	if rec.Code != http.StatusUnauthorized {
		t.Fatalf("status = %d, want 401", rec.Code)
	}
	body := decode[restgen.Error](t, rec)
	if body.Code != string(codeUnauthenticated) {
		t.Errorf("code = %q, want %q", body.Code, codeUnauthenticated)
	}
}

func TestListChangelog_RejectsAGarbageToken(t *testing.T) {
	h := newHarness(t)
	rec := h.get(t, "/rest/v1/changelog", map[string]string{"Authorization": "Bearer not-a-jwt"})
	if rec.Code != http.StatusUnauthorized {
		t.Fatalf("status = %d, want 401", rec.Code)
	}
}

func TestListChangelog_ServesReleasesPrivatelyCached(t *testing.T) {
	h := newHarness(t)
	relID := uuid.New()
	now := time.Date(2026, 8, 26, 12, 0, 0, 0, time.UTC)
	h.clog.releases = []db.ChangelogRelease{{ID: relID, Component: "web", Version: "1.30.0", ReleasedAt: ts(now), CreatedAt: ts(now)}}
	h.clog.items = []db.ChangelogItem{{ID: uuid.New(), ReleaseID: relID, ChangeType: "added", SummaryEn: "REST endpoints", SummaryEs: "Puntos REST"}}

	rec := h.get(t, "/rest/v1/changelog", map[string]string{"Authorization": h.token(t)})
	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200 (body: %s)", rec.Code, rec.Body.String())
	}
	body := decode[restgen.ChangelogResponse](t, rec)
	if len(body.Releases) != 1 || body.Releases[0].Version != "1.30.0" {
		t.Fatalf("releases = %+v", body.Releases)
	}
	if body.Releases[0].Component != "web" || len(body.Releases[0].Items) != 1 || body.Releases[0].Items[0].ChangeType != "added" {
		t.Errorf("release = %+v; the text columns should map straight onto the enums", body.Releases[0])
	}
	if body.CurrentServerVersion == "" {
		t.Error("currentServerVersion is empty; the what's-new prompt keys off it")
	}
	// Private only: a shared cache would need Vary: Authorization, which
	// fragments per token and saves nothing.
	if got := rec.Header().Get("Cache-Control"); got != cacheChangelog {
		t.Errorf("Cache-Control = %q, want %q", got, cacheChangelog)
	}
}

func TestChangelogETag_ChangesWhenOnlyTheServerVersionMoves(t *testing.T) {
	// A deploy that publishes no new notes still changes the response, because
	// currentServerVersion travels in the body.
	base := restgen.ChangelogResponse{CurrentServerVersion: "1.0.0"}
	bumped := restgen.ChangelogResponse{CurrentServerVersion: "1.1.0"}
	if changelogETag(base) == changelogETag(bumped) {
		t.Error("ETag ignored a server-version bump")
	}
}

func TestListChangelog_MalformedQueryParamIsA400InTheContractsErrorShape(t *testing.T) {
	// The generated default writes plain text via http.Error; a client
	// generated from the same contract expects the Error schema everywhere.
	h := newHarness(t)
	rec := h.get(t, "/rest/v1/changelog?limitPerComponent=lots", map[string]string{"Authorization": h.token(t)})

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want 400", rec.Code)
	}
	body := decode[restgen.Error](t, rec)
	if body.Code != string(codeInvalidArgument) {
		t.Errorf("code = %q, want %q", body.Code, codeInvalidArgument)
	}
}

// ─── pure helpers ────────────────────────────────────────────────────────────

func TestMatchesETag(t *testing.T) {
	const tag = `"abc123"`
	cases := []struct {
		name        string
		ifNoneMatch string
		want        bool
	}{
		{"exact", `"abc123"`, true},
		{"absent", "", false},
		{"different", `"def456"`, false},
		{"wildcard", "*", true},
		{"list containing it", `"zzz", "abc123", "yyy"`, true},
		{"list without it", `"zzz", "yyy"`, false},
		{"weak on the request side", `W/"abc123"`, true},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			if got := matchesETag(tc.ifNoneMatch, tag); got != tc.want {
				t.Errorf("matchesETag(%q, %q) = %v, want %v", tc.ifNoneMatch, tag, got, tc.want)
			}
		})
	}
}

func TestMakeETag_IsStableAndSeparatesFields(t *testing.T) {
	if makeETag("a", "b") != makeETag("a", "b") {
		t.Error("not stable across calls")
	}
	// Without a separator "ab"+"c" and "a"+"bc" would collide, which would let
	// two genuinely different resources share a tag.
	if makeETag("ab", "c") == makeETag("a", "bc") {
		t.Error("field boundaries are not encoded")
	}
	if got := makeETag("x"); got[0] != '"' || got[len(got)-1] != '"' {
		t.Errorf("makeETag = %s, want a quoted entity tag", got)
	}
}

func TestStatusForError_MirrorsTheConnectMapping(t *testing.T) {
	cases := []struct {
		name string
		err  error
		want int
		code errorCode
	}{
		{"not found", apperr.NotFound("country", "ZZ"), http.StatusNotFound, codeNotFound},
		{"forbidden", apperr.Forbidden("nope"), http.StatusForbidden, codeForbidden},
		{"duplicate", apperr.Duplicate("user", "email", "a@b.c"), http.StatusConflict, codeAlreadyExists},
		{"invalid", apperr.Invalid("bad"), http.StatusBadRequest, codeInvalidArgument},
		{"unknown", context.Canceled, http.StatusInternalServerError, codeInternal},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			status, code := statusForError(tc.err)
			if status != tc.want || code != tc.code {
				t.Errorf("statusForError = (%d, %q), want (%d, %q)", status, code, tc.want, tc.code)
			}
		})
	}
}
