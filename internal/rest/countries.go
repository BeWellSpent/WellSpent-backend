package rest

import (
	"net/http"
	"strconv"

	restgen "github.com/BeWellSpent/wellspent-backend/gen/rest"
)

// ListCountries serves the country registry with its per-country feature flags.
//
// The canonical case for this transport: unauthenticated, three rows, identical
// for every caller, changed by a migration and not otherwise. Cached for a day
// with a week of stale-while-revalidate, so a returning visitor effectively
// never waits on it.
func (s *server) ListCountries(w http.ResponseWriter, r *http.Request, _ restgen.ListCountriesParams) {
	rows, featuresByCode, err := s.deps.Users.ListCountries(r.Context())
	if err != nil {
		writeError(w, r, s.deps.Logger, err)
		return
	}

	body := toRESTCountries(rows, featuresByCode)
	writeJSON(w, r, s.deps.Logger, cacheCountries, countriesETag(body), body)
}

// countriesETag fingerprints every field a client can observe.
//
// Built from the response data rather than its JSON bytes so the tag survives a
// change to the encoder or a field reordering, and covers the feature flags as
// well as the countries — enabling `before_tax_income` for a country changes
// what the registration form renders, and a tag keyed only on country codes
// would serve the old answer for a full day.
func countriesETag(body restgen.CountriesResponse) string {
	parts := make([]string, 0, len(body.Countries)*4)
	for _, c := range body.Countries {
		parts = append(parts, c.Code, c.Name, strconv.FormatBool(c.IsEnabled))
		for _, f := range c.Features {
			parts = append(parts, f.Name, strconv.FormatBool(f.IsEnabled))
		}
	}
	return makeETag(parts...)
}
