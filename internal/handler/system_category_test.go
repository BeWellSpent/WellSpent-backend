package handler

import (
	"testing"

	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/BeWellSpent/wellspent-backend/internal/category"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// A key with no entry in systemCategoryByKey serializes as UNSPECIFIED, which
// every client reads as "user-created, render Category.name" — so the category
// silently stops being translated and stops being special-cased, with nothing
// logged. Assert the map is total over the key set rather than waiting to
// notice in the UI.
func TestEverySystemKeyMapsToADistinctEnumValue(t *testing.T) {
	seen := make(map[v1.SystemCategory]category.Key, len(category.All))
	for _, k := range category.All {
		got := protoSystemCategory(ptr(string(k)))
		require.NotEqualf(t, v1.SystemCategory_SYSTEM_CATEGORY_UNSPECIFIED, got,
			"key %q maps to UNSPECIFIED — clients will fall back to the English name", k)
		if prev, dup := seen[got]; dup {
			t.Errorf("keys %q and %q both map to %v", prev, k, got)
		}
		seen[got] = k
	}
}

func TestProtoSystemCategory_NilKeyIsUnspecified(t *testing.T) {
	assert.Equal(t, v1.SystemCategory_SYSTEM_CATEGORY_UNSPECIFIED, protoSystemCategory(nil))
}

// An unknown key means this build is older than the row it just read. Falling
// back to UNSPECIFIED is the intended degradation: the client renders the
// English name rather than a raw key.
func TestProtoSystemCategory_UnknownKeyIsUnspecified(t *testing.T) {
	assert.Equal(t, v1.SystemCategory_SYSTEM_CATEGORY_UNSPECIFIED, protoSystemCategory(ptr("seeded_after_this_build")))
}

func TestToProtoCategory_UserCategoryKeepsNameAndIsUnspecified(t *testing.T) {
	got := toProtoCategory(7, "Cycling", false, "#abc", nil)
	assert.Equal(t, int32(7), got.Id)
	assert.Equal(t, "Cycling", got.Name)
	assert.False(t, got.IsSystem)
	assert.Equal(t, v1.SystemCategory_SYSTEM_CATEGORY_UNSPECIFIED, got.SystemCategory)
}

// name stays populated on a system category too: it is what a client renders
// when it doesn't recognise the enum value.
func TestToProtoCategory_SystemCategoryCarriesBothEnumAndEnglishName(t *testing.T) {
	got := toProtoCategory(3, "Eating Out", true, "", ptr(string(category.EatingOut)))
	assert.Equal(t, "Eating Out", got.Name)
	assert.True(t, got.IsSystem)
	assert.Equal(t, v1.SystemCategory_SYSTEM_CATEGORY_EATING_OUT, got.SystemCategory)
}

func ptr(s string) *string { return &s }
