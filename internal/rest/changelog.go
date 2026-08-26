package rest

import (
	"net/http"
	"strconv"

	restgen "github.com/BeWellSpent/wellspent-backend/gen/rest"
)

// ListChangelog serves published release notes.
//
// The one authenticated endpoint on this transport. It qualifies because the
// response is the same for every signed-in caller — the token gates access, it
// does not shape the result — but that only permits *private* caching: a shared
// cache would need `Vary: Authorization`, which fragments it per token and
// saves nothing.
func (s *server) ListChangelog(w http.ResponseWriter, r *http.Request, params restgen.ListChangelogParams) {
	if _, ok := s.requireAuth(w, r); !ok {
		return
	}

	var components []string
	if params.Component != nil {
		components = make([]string, 0, len(*params.Component))
		for _, c := range *params.Component {
			components = append(components, string(c))
		}
	}

	var limit int32
	if params.LimitPerComponent != nil {
		limit = *params.LimitPerComponent
	}

	releases, err := s.deps.Changelog.ListReleases(r.Context(), components, limit)
	if err != nil {
		writeError(w, r, s.deps.Logger, err)
		return
	}

	body := toRESTChangelog(releases, s.deps.Changelog.ServerVersion())
	writeJSON(w, r, s.deps.Logger, cacheChangelog, changelogETag(body), body)
}

// changelogETag fingerprints the releases returned and the running server
// version.
//
// The query parameters are not folded in: an ETag is only ever compared against
// the same URL, and the parameters are part of that URL. The server version is
// folded in because it travels in the body — a deploy that publishes no new
// notes still changes the response, and a reader's "what's new" prompt keys off
// exactly that number.
func changelogETag(body restgen.ChangelogResponse) string {
	parts := make([]string, 0, len(body.Releases)*2+1)
	parts = append(parts, body.CurrentServerVersion)
	for _, rel := range body.Releases {
		// Release rows are append-only, but items can be added to one after
		// the fact, so the item count is part of the identity.
		parts = append(parts, rel.Id.String(), strconv.Itoa(len(rel.Items)))
	}
	return makeETag(parts...)
}
