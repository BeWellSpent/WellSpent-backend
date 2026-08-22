package category

import (
	"os"
	"strings"
	"testing"
)

// The migration is the source of truth for what is actually in the database.
// If a system category is seeded without a matching Key, every lookup for it
// silently misses; if a Key exists with no seeded row, every lookup for it
// silently returns nothing. Neither raises an error at runtime — that is the
// entire class of bug issue #49 is about — so it has to be caught here.
const migrationPath = "../db/migrations/000054_category_system_key.sql"

func TestEveryKeyIsBackfilledByTheMigration(t *testing.T) {
	body, err := os.ReadFile(migrationPath)
	if err != nil {
		t.Fatalf("read migration: %v", err)
	}
	sql := string(body)
	for _, k := range All {
		if !strings.Contains(sql, "'"+string(k)+"'") {
			t.Errorf("key %q has no backfill row in %s — lookups for it will silently find nothing", k, migrationPath)
		}
	}
}

func TestMigrationBackfillsNoKeyMissingFromAll(t *testing.T) {
	body, err := os.ReadFile(migrationPath)
	if err != nil {
		t.Fatalf("read migration: %v", err)
	}
	known := make(map[Key]bool, len(All))
	for _, k := range All {
		known[k] = true
	}
	// Backfill rows look like:  ('Eating Out',     'eating_out'),
	for _, line := range strings.Split(string(body), "\n") {
		trimmed := strings.TrimSpace(line)
		if !strings.HasPrefix(trimmed, "('") {
			continue
		}
		parts := strings.Split(trimmed, "'")
		if len(parts) < 4 {
			continue
		}
		if key := Key(parts[3]); !known[key] {
			t.Errorf("migration seeds key %q with no matching constant in this package", key)
		}
	}
}

func TestAllContainsNoDuplicates(t *testing.T) {
	seen := make(map[Key]bool, len(All))
	for _, k := range All {
		if seen[k] {
			t.Errorf("duplicate key %q in All", k)
		}
		seen[k] = true
	}
	if len(All) != 25 {
		t.Errorf("All has %d keys, expected 25 — update this count deliberately when seeding a new system category", len(All))
	}
}
