// Package category holds the stable keys identifying the app's seeded system
// categories.
//
// These exist because a system category used to be identified by its English
// display name -- `name == "Income"` -- in twenty places across this backend
// and both clients. That is what made the names impossible to localize: the
// moment `name` came back translated, every one of those comparisons would
// silently stop matching, and Income would stop being excluded from spending
// totals with no error raised anywhere.
//
// A Key is the value in `category.system_key` (backend migration 000054) and
// maps 1:1 to v1.SystemCategory on the wire. `category.name` is now purely a
// display fallback.
package category

// Key identifies one seeded system category, independent of display language.
type Key string

// The complete set, in the order the categories were seeded. Keep in sync with
// migration 000054's backfill and with v1.SystemCategory -- TestKeysMatchMigration
// and TestEveryKeyMapsToProtoEnum enforce both.
const (
	Entertainment  Key = "entertainment"
	Insurance      Key = "insurance"
	Loan           Key = "loan"
	Wellness       Key = "wellness"
	Services       Key = "services"
	Subscription   Key = "subscription"
	Rent           Key = "rent"
	Travel         Key = "travel"
	EatingOut      Key = "eating_out"
	Groceries      Key = "groceries"
	Baby           Key = "baby"
	Pet            Key = "pet"
	Misc           Key = "misc"
	House          Key = "house"
	Gas            Key = "gas"
	Auto           Key = "auto"
	Savings        Key = "savings"
	Shopping       Key = "shopping"
	Family         Key = "family"
	Income         Key = "income"
	Payment        Key = "payment"
	Transfer       Key = "transfer"
	Transportation Key = "transportation"
	Utilities      Key = "utilities"
	Debt           Key = "debt"
)

// All lists every system category key. Used by the tests that guard this
// package against drifting from the migration or the proto enum.
var All = []Key{
	Entertainment, Insurance, Loan, Wellness, Services, Subscription, Rent,
	Travel, EatingOut, Groceries, Baby, Pet, Misc, House, Gas, Auto, Savings,
	Shopping, Family, Income, Payment, Transfer, Transportation, Utilities,
	Debt,
}
