package rest

import (
	"net/http"

	restgen "github.com/BeWellSpent/wellspent-backend/gen/rest"
)

// noBannerETag is the tag for "nothing is live".
//
// A stable sentinel rather than an empty tag, because the quiet case is the
// overwhelmingly common one: without it, every client on every page load would
// get an unconditional 200 for the response that never changes, which is the
// opposite of the point.
const noBannerETag = "none"

// GetActiveStatusBanner serves the banner currently in effect, if any.
//
// Public on purpose — a signed-out visitor looking at a broken login screen is
// one of the people who most needs to read it. Cached for only 30 seconds:
// a banner exists to appear during an incident, and 30 seconds is the most
// delay worth trading for taking this off the origin on every page load.
func (s *server) GetActiveStatusBanner(w http.ResponseWriter, r *http.Request, _ restgen.GetActiveStatusBannerParams) {
	banner, found, err := s.deps.StatusBanners.GetActive(r.Context())
	if err != nil {
		writeError(w, r, s.deps.Logger, err)
		return
	}

	// Nothing live is a successful empty response, not a 404 — same rule the
	// retired RPC followed, and for the same reason.
	if !found {
		writeJSON(w, r, s.deps.Logger, cacheStatusBanner, makeETag(noBannerETag), restgen.StatusBannerResponse{})
		return
	}

	out := toRESTStatusBanner(banner)
	// Keyed on id and ends_at, not created_at: rows are never edited, but
	// ExpireStatusBanner takes a banner down early by moving ends_at, and a tag
	// that ignored that would keep serving a retracted notice for the whole
	// max-age. (There is no updated_at column on this table to key off.)
	etag := makeETag(out.Id.String(), out.EndsAt.String())
	writeJSON(w, r, s.deps.Logger, cacheStatusBanner, etag, restgen.StatusBannerResponse{Banner: &out})
}
