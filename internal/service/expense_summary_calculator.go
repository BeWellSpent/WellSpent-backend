package service

import (
	"sort"
	"time"

	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/google/uuid"
	"google.golang.org/protobuf/types/known/timestamppb"
)

// expenseSummaryCalculator turns an already-fetched expenseSummaryData into
// a GetExpenseSummaryResponse. It does no I/O — every method here is pure,
// which is what makes the planned-total fallback chain (allocation -> due
// Fixed transaction) and the Plan-vs-Overview visibility rules safe to unit
// test directly, without mock repos, the same way the two client codebases
// used to test their own (before this RPC existed) copies of this exact
// logic.
type expenseSummaryCalculator struct {
	data *expenseSummaryData

	// Per-category / per-(category,person) actual spend.
	actualByCat       map[int32]int64
	actualByPersonCat map[catPersonKey]int64
	uncategorized     int64

	// Tier 1: allocations.
	allocByCat       map[int32]int64
	allocByPersonCat map[catPersonKey]int64
	catIDsWithAlloc  map[int32]bool

	// Tier 2: Fixed transactions due (spawned) this period.
	fixedDueByCat       map[int32]int64
	fixedDueByPersonCat map[catPersonKey]int64

	// Active Fixed expense templates not yet due this period. Informational
	// only — never a planned-total tier and never part of any aggregate: an
	// obligation that isn't due yet isn't owed against this period, so
	// counting it inflates the plan (issue #48). It used to be a third
	// fallback tier feeding the Overview's total_planned while the Plan
	// tab's total_committed excluded it, which left the two tabs disagreeing
	// and neither total matching the sum of its own rows.
	//
	// It still travels to the clients as CategoryExpenseSummary's
	// not_due_planned_total / next_due_date so a category doesn't silently
	// vanish between due periods.
	notDueFixedByCat   map[int32]int64
	notDueNextDueByCat map[int32]time.Time
	fixedExpenseCatIDs map[int32]bool

	// Savings: system-managed category, amount is the sum of savings source
	// amounts regardless of allocations/fixed expenses.
	savingsTotal    int64
	savingsByPerson map[int32]int64

	pmPersonMap map[uuid.UUID]int32
}

func newExpenseSummaryCalculator(data *expenseSummaryData) *expenseSummaryCalculator {
	c := &expenseSummaryCalculator{data: data}
	c.computePersonMap()
	c.computeActuals()
	c.computePlannedTiers()
	c.computeSavings()
	return c
}

func (c *expenseSummaryCalculator) computePersonMap() {
	c.pmPersonMap = make(map[uuid.UUID]int32, len(c.data.paymentMethods))
	for _, pm := range c.data.paymentMethods {
		if pm.BudgetPersonID != nil {
			c.pmPersonMap[pm.ID] = *pm.BudgetPersonID
		}
	}
}

// computeActuals sums each already-exclusion-filtered transaction's actual
// amount by category (and by person+category). Unpaid Fixed transactions
// don't count as spent yet; uncategorized transactions accumulate
// separately rather than being dropped.
func (c *expenseSummaryCalculator) computeActuals() {
	c.actualByCat = map[int32]int64{}
	c.actualByPersonCat = map[catPersonKey]int64{}
	for _, tx := range c.data.transactions {
		if isUnpaidFixed(tx) {
			continue
		}
		amt := numericToNanos(tx.Amount)
		if tx.CategoryID == nil {
			c.uncategorized += amt
			continue
		}
		c.actualByCat[*tx.CategoryID] += amt
		if tx.PaymentMethodID != nil {
			if personID, ok := c.pmPersonMap[*tx.PaymentMethodID]; ok {
				c.actualByPersonCat[catPersonKey{*tx.CategoryID, personID}] += amt
			}
		}
	}
}

// computePlannedTiers builds all three planned-amount fallback tiers:
// allocations, Fixed transactions due this period, and active Fixed expense
// templates not yet due.
func (c *expenseSummaryCalculator) computePlannedTiers() {
	c.allocByCat = map[int32]int64{}
	c.allocByPersonCat = map[catPersonKey]int64{}
	c.catIDsWithAlloc = map[int32]bool{}
	for _, a := range c.data.allocations {
		amt := numericToNanos(a.PlannedAmount)
		c.allocByCat[a.CategoryID] += amt
		c.catIDsWithAlloc[a.CategoryID] = true
		if a.BudgetPersonID != nil {
			c.allocByPersonCat[catPersonKey{a.CategoryID, *a.BudgetPersonID}] += amt
		}
	}

	c.fixedDueByCat = map[int32]int64{}
	c.fixedDueByPersonCat = map[catPersonKey]int64{}
	for _, tx := range c.data.transactions {
		if tx.TransactionTypeID == nil || *tx.TransactionTypeID != fixedTransactionTypeID || tx.CategoryID == nil {
			continue
		}
		amt := numericToNanos(tx.PlannedAmount)
		c.fixedDueByCat[*tx.CategoryID] += amt
		if tx.PaymentMethodID != nil {
			if personID, ok := c.pmPersonMap[*tx.PaymentMethodID]; ok {
				c.fixedDueByPersonCat[catPersonKey{*tx.CategoryID, personID}] += amt
			}
		}
	}

	c.notDueFixedByCat = map[int32]int64{}
	c.notDueNextDueByCat = map[int32]time.Time{}
	c.fixedExpenseCatIDs = map[int32]bool{}
	for _, fe := range c.data.activeFixedExpenses {
		if fe.CategoryID == nil {
			continue
		}
		c.fixedExpenseCatIDs[*fe.CategoryID] = true
		if _, ok := c.fixedDueByCat[*fe.CategoryID]; ok {
			continue
		}
		c.notDueFixedByCat[*fe.CategoryID] += numericToNanos(fe.PlannedAmount)
		// Earliest wins: several templates can share a category, and the
		// caption answers "when does this category next cost me anything".
		due := FixedExpenseNextDueDate(fe, c.data.now)
		if existing, ok := c.notDueNextDueByCat[*fe.CategoryID]; !ok || due.Before(existing) {
			c.notDueNextDueByCat[*fe.CategoryID] = due
		}
	}
}

func (c *expenseSummaryCalculator) computeSavings() {
	c.savingsByPerson = map[int32]int64{}
	for _, ss := range c.data.savingsSources {
		amt := numericToNanos(ss.Amount)
		c.savingsTotal += amt
		if ss.BudgetPersonID != nil {
			c.savingsByPerson[*ss.BudgetPersonID] += amt
		}
	}
}

func (c *expenseSummaryCalculator) isSavingsCategory(catID int32) bool {
	return c.data.hasSavingsCategory && catID == c.data.savingsCategoryID
}

// plannedTotal mirrors web's getCatPlanned/getCategoryPlanned (identical on
// both the Plan and Overview tabs): savings first, then allocations, then
// Fixed transactions due this period.
//
// A not-yet-due Fixed template is deliberately NOT a further fallback — it
// reports separately via notDuePlannedTotal (see notDueFixedByCat).
func (c *expenseSummaryCalculator) plannedTotal(catID int32) int64 {
	if c.isSavingsCategory(catID) {
		return c.savingsTotal
	}
	if alloc, ok := c.allocByCat[catID]; ok && alloc != 0 {
		return alloc
	}
	return c.fixedDueByCat[catID]
}

// notDueSummary returns the informational not-due fields for a category, both
// nil when nothing is pending, so the wire stays empty for the common case.
func (c *expenseSummaryCalculator) notDueSummary(catID int32) (*v1.Money, *timestamppb.Timestamp) {
	amount := c.notDueFixedByCat[catID]
	if amount == 0 {
		return nil, nil
	}
	var nextDue *timestamppb.Timestamp
	if due, ok := c.notDueNextDueByCat[catID]; ok {
		nextDue = timestamppb.New(due)
	}
	return nanosToMoney(amount), nextDue
}

func (c *expenseSummaryCalculator) plannedTotalForPerson(catID, personID int32) int64 {
	if c.isSavingsCategory(catID) {
		return c.savingsByPerson[personID]
	}
	if alloc, ok := c.allocByPersonCat[catPersonKey{catID, personID}]; ok && alloc != 0 {
		return alloc
	}
	return c.fixedDueByPersonCat[catPersonKey{catID, personID}]
}

// allCategoryIDs is the union of every category ID any source references —
// the candidate set both buildPlanCategories and buildOverviewCategories
// filter down from.
func (c *expenseSummaryCalculator) allCategoryIDs() []int32 {
	seen := map[int32]bool{}
	for id := range c.actualByCat {
		seen[id] = true
	}
	for id := range c.allocByCat {
		seen[id] = true
	}
	for id := range c.fixedDueByCat {
		seen[id] = true
	}
	for id := range c.notDueFixedByCat {
		seen[id] = true
	}
	if c.data.hasSavingsCategory && len(c.data.savingsSources) > 0 {
		seen[c.data.savingsCategoryID] = true
	}
	ids := make([]int32, 0, len(seen))
	for id := range seen {
		ids = append(ids, id)
	}
	return ids
}

func (c *expenseSummaryCalculator) buildPersonBreakdowns(catID int32) []*v1.PersonExpenseSummary {
	var out []*v1.PersonExpenseSummary
	for _, p := range c.data.people {
		planned := c.plannedTotalForPerson(catID, p.ID)
		actual := c.actualByPersonCat[catPersonKey{catID, p.ID}]
		if planned == 0 && actual == 0 {
			continue
		}
		out = append(out, &v1.PersonExpenseSummary{
			BudgetPersonId: int64(p.ID),
			PlannedTotal:   nanosToMoney(planned),
			ActualTotal:    nanosToMoney(actual),
		})
	}
	return out
}

// buildPlanCategories returns the Expense Plan tab's category rows (visible
// if allocated, due this period, or templated — regardless of due status —
// sorted by planned descending) plus total_committed.
//
// A not-yet-due template keeps its row visible but contributes a planned
// total of zero, carrying its amount in not_due_planned_total instead. That
// makes total_committed exactly the sum of the rows above it, which it
// previously wasn't: the rows included the not-due tier and the total didn't,
// so a user adding up the list got a larger number than the total shown.
func (c *expenseSummaryCalculator) buildPlanCategories() (categories []*v1.CategoryExpenseSummary, totalCommitted int64) {
	for _, catID := range c.allCategoryIDs() {
		hasSavingsSources := c.isSavingsCategory(catID) && len(c.data.savingsSources) > 0
		visible := c.catIDsWithAlloc[catID] || hasSavingsSources || c.fixedDueByCat[catID] != 0 || c.fixedExpenseCatIDs[catID]
		if !visible {
			continue
		}

		planned := c.plannedTotal(catID)
		notDue, nextDue := c.notDueSummary(catID)
		categories = append(categories, &v1.CategoryExpenseSummary{
			CategoryId:         catID,
			PlannedTotal:       nanosToMoney(planned),
			NotDuePlannedTotal: notDue,
			NextDueDate:        nextDue,
			PersonBreakdowns:   c.buildPersonBreakdowns(catID),
		})

		totalCommitted += planned
	}
	sort.Slice(categories, func(i, j int) bool {
		a, b := categories[i].PlannedTotal, categories[j].PlannedTotal
		return a.Units > b.Units || (a.Units == b.Units && a.Nanos > b.Nanos)
	})
	return categories, totalCommitted
}

// buildOverviewCategories returns the Expense Overview tab's category rows
// (visible if there's actual spend OR any plan — so unspent budget stays
// visible too — sorted by actual descending) plus total_planned,
// total_actual, total_over_budget, and total_unplanned.
//
// A category whose only obligation is a not-yet-due template is absent here.
// It has no actual spend and, since that tier stopped feeding plannedTotal,
// no plan either — a 0/0 row on a tab about what was actually spent. It
// still appears on the Plan tab, which is where an upcoming bill belongs.
func (c *expenseSummaryCalculator) buildOverviewCategories() (categories []*v1.CategoryExpenseSummary, totalPlanned, totalActual, totalOverBudget, totalUnplanned int64) {
	totalActual = c.uncategorized
	totalUnplanned = c.uncategorized

	for _, catID := range c.allCategoryIDs() {
		hasSavingsSources := c.isSavingsCategory(catID) && len(c.data.savingsSources) > 0
		visible := c.actualByCat[catID] != 0 || c.catIDsWithAlloc[catID] || hasSavingsSources ||
			c.fixedDueByCat[catID] != 0
		if !visible {
			continue
		}

		planned := c.plannedTotal(catID)
		actual := c.actualByCat[catID]
		isOver := planned > 0 && actual > planned

		categories = append(categories, &v1.CategoryExpenseSummary{
			CategoryId:       catID,
			PlannedTotal:     nanosToMoney(planned),
			ActualTotal:      nanosToMoney(actual),
			IsOver:           isOver,
			PersonBreakdowns: c.buildPersonBreakdowns(catID),
		})

		totalPlanned += planned
		totalActual += actual
		if planned <= 0 {
			totalUnplanned += actual
		} else if actual > planned {
			totalOverBudget += actual - planned
		}
	}
	sort.Slice(categories, func(i, j int) bool {
		a, b := categories[i].ActualTotal, categories[j].ActualTotal
		return a.Units > b.Units || (a.Units == b.Units && a.Nanos > b.Nanos)
	})
	return categories, totalPlanned, totalActual, totalOverBudget, totalUnplanned
}

func (c *expenseSummaryCalculator) response() *v1.GetExpenseSummaryResponse {
	planCategories, totalCommitted := c.buildPlanCategories()
	overviewCategories, totalPlanned, totalActual, totalOverBudget, totalUnplanned := c.buildOverviewCategories()

	var incomeFromSources int64
	for _, is := range c.data.incomeSources {
		incomeFromSources += numericToNanos(is.DefaultAmount)
	}
	var incomeFromEntries int64
	for _, ie := range c.data.incomeEntries {
		incomeFromEntries += numericToNanos(ie.Amount)
	}

	return &v1.GetExpenseSummaryResponse{
		IncomeFromSources:   nanosToMoney(incomeFromSources),
		IncomeFromEntries:   nanosToMoney(incomeFromEntries),
		TotalCommitted:      nanosToMoney(totalCommitted),
		TotalPlanned:        nanosToMoney(totalPlanned),
		TotalActual:         nanosToMoney(totalActual),
		UncategorizedActual: nanosToMoney(c.uncategorized),
		TotalOverBudget:     nanosToMoney(totalOverBudget),
		TotalUnplanned:      nanosToMoney(totalUnplanned),
		RemainderPlan:       nanosToMoney(incomeFromSources - totalCommitted),
		RemainderActual:     nanosToMoney(incomeFromEntries - totalActual),
		RemainderPlanned:    nanosToMoney(incomeFromEntries - totalPlanned),
		PlanCategories:      planCategories,
		OverviewCategories:  overviewCategories,
	}
}
