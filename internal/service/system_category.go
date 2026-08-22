package service

import (
	"github.com/BeWellSpent/wellspent-backend/internal/category"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
)

// findSystemCategoryID returns the ID of the seeded system category with the
// given key, or nil when this user's category list doesn't contain it.
//
// Replaces three near-identical loops that matched on `c.Name == "Savings"`.
// Matching on the English name is what made the names impossible to localize
// (issue #49) — and a name comparison that stops matching fails silently, by
// simply never finding the category, so the savings rows would quietly stop
// being cleaned up rather than erroring.
func findSystemCategoryID(cats []db.ListCategoriesRow, key category.Key) *int32 {
	for _, c := range cats {
		if c.IsSystem && c.SystemKey != nil && category.Key(*c.SystemKey) == key {
			id := c.ID
			return &id
		}
	}
	return nil
}
