using WellSpent.Application.Common;
using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.ExpenseSummary;

/// <summary>
/// Turns an already-fetched ExpenseSummaryData into a GetExpenseSummaryDto.
/// Does no I/O — every method here is pure, mirroring Go's
/// expenseSummaryCalculator exactly (including its three-tier planned-total
/// fallback chain and the Plan-vs-Overview visibility rules), modulo one
/// simplification: Go sums onto an int64-nanos scale specifically to avoid a
/// lossy float64 intermediate; C#'s decimal is already exact, so every total
/// here is summed as a plain decimal.
/// </summary>
public sealed class ExpenseSummaryCalculator
{
    private readonly ExpenseSummaryData _data;

    // Per-category / per-(category,person) actual spend.
    private readonly Dictionary<int, decimal> _actualByCat = new();
    private readonly Dictionary<(int CatId, int PersonId), decimal> _actualByPersonCat = new();
    private decimal _uncategorized;

    // Tier 1: allocations.
    private readonly Dictionary<int, decimal> _allocByCat = new();
    private readonly Dictionary<(int CatId, int PersonId), decimal> _allocByPersonCat = new();
    private readonly HashSet<int> _catIdsWithAlloc = new();

    // Tier 2: Fixed transactions due (spawned) this period.
    private readonly Dictionary<int, decimal> _fixedDueByCat = new();
    private readonly Dictionary<(int CatId, int PersonId), decimal> _fixedDueByPersonCat = new();

    // Active Fixed expense templates not yet due this period — informational
    // only, never a planned-total tier and never part of any aggregate (an
    // obligation that isn't due yet isn't owed against this period).
    private readonly Dictionary<int, decimal> _notDueFixedByCat = new();
    private readonly Dictionary<int, DateOnly> _notDueNextDueByCat = new();
    private readonly HashSet<int> _fixedExpenseCatIds = new();

    // Savings: system-managed category, amount is the sum of savings source
    // amounts regardless of allocations/fixed expenses.
    private decimal _savingsTotal;
    private readonly Dictionary<int, decimal> _savingsByPerson = new();

    // total_actual split by transaction type, under the same filter.
    private decimal _fixedActual;
    private decimal _variableActual;

    // Variable transactions that pushed their category past plannedTotal.
    private readonly List<Guid> _overBudgetTxIds = new();

    private readonly Dictionary<Guid, int> _pmPersonMap = new();

    public ExpenseSummaryCalculator(ExpenseSummaryData data)
    {
        _data = data;
        ComputePersonMap();
        ComputeActuals();
        ComputePlannedTiers();
        ComputeSavings();
        // Depends on both planned tiers and savings, so it runs last.
        ComputeOverBudget();
    }

    private void ComputePersonMap()
    {
        foreach (var pm in _data.PaymentMethods)
        {
            if (pm.BudgetPersonId is { } personId) _pmPersonMap[pm.Id] = personId;
        }
    }

    /// <summary>Sums each already-exclusion-filtered transaction's actual amount by category (and by person+category). Unpaid Fixed transactions don't count as spent yet; uncategorized transactions accumulate separately rather than being dropped.</summary>
    private void ComputeActuals()
    {
        foreach (var tx in _data.Transactions)
        {
            if (SpendFilter.IsUnpaidFixed(tx)) continue;
            var amt = tx.Amount;
            // Split before the uncategorized branch, so the two halves still sum to total_actual when a transaction has no category.
            if (SpendFilter.IsFixed(tx)) _fixedActual += amt; else _variableActual += amt;

            if (tx.CategoryId is not { } catId)
            {
                _uncategorized += amt;
                continue;
            }
            _actualByCat[catId] = _actualByCat.GetValueOrDefault(catId) + amt;
            if (tx.PaymentMethodId is { } pmId && _pmPersonMap.TryGetValue(pmId, out var personId))
            {
                var key = (catId, personId);
                _actualByPersonCat[key] = _actualByPersonCat.GetValueOrDefault(key) + amt;
            }
        }
    }

    /// <summary>Builds all three planned-amount fallback tiers: allocations, Fixed transactions due this period, and active Fixed expense templates not yet due.</summary>
    private void ComputePlannedTiers()
    {
        foreach (var a in _data.Allocations)
        {
            _allocByCat[a.CategoryId] = _allocByCat.GetValueOrDefault(a.CategoryId) + a.PlannedAmount;
            _catIdsWithAlloc.Add(a.CategoryId);
            if (a.BudgetPersonId is { } personId)
            {
                var key = (a.CategoryId, personId);
                _allocByPersonCat[key] = _allocByPersonCat.GetValueOrDefault(key) + a.PlannedAmount;
            }
        }

        // Which templates already have a bill spawned into this period.
        // Tracked per template rather than per category because a category
        // routinely holds both a monthly bill and an annual one.
        var dueFixedExpenseIds = new HashSet<Guid>();
        foreach (var tx in _data.Transactions)
        {
            if (!SpendFilter.IsFixed(tx)) continue;
            // Recorded before the category check: a spawned transaction
            // proves its template is owed this period whether or not it
            // carries a category.
            if (tx.FixedExpenseId is { } feId) dueFixedExpenseIds.Add(feId);
            if (tx.CategoryId is not { } catId) continue;

            _fixedDueByCat[catId] = _fixedDueByCat.GetValueOrDefault(catId) + tx.PlannedAmount;
            if (tx.PaymentMethodId is { } pmId && _pmPersonMap.TryGetValue(pmId, out var personId))
            {
                var key = (catId, personId);
                _fixedDueByPersonCat[key] = _fixedDueByPersonCat.GetValueOrDefault(key) + tx.PlannedAmount;
            }
        }

        foreach (var fe in _data.ActiveFixedExpenses)
        {
            if (fe.CategoryId is not { } catId) continue;
            _fixedExpenseCatIds.Add(catId);
            // Skip this template only if *its own* bill is due this period —
            // not every template in a category that has one due (that used
            // to silently swallow an upcoming annual bill sharing a category
            // with a monthly one).
            if (dueFixedExpenseIds.Contains(fe.Id)) continue;

            _notDueFixedByCat[catId] = _notDueFixedByCat.GetValueOrDefault(catId) + fe.PlannedAmount;
            // Earliest wins: several templates can share a category, and the
            // caption answers "when does this category next cost me anything".
            var due = FixedExpenseScheduling.NextDueDate(fe, _data.Now);
            if (!_notDueNextDueByCat.TryGetValue(catId, out var existing) || due < existing)
            {
                _notDueNextDueByCat[catId] = due;
            }
        }
    }

    private void ComputeSavings()
    {
        foreach (var ss in _data.SavingsSources)
        {
            _savingsTotal += ss.Amount;
            if (ss.BudgetPersonId is { } personId)
            {
                _savingsByPerson[personId] = _savingsByPerson.GetValueOrDefault(personId) + ss.Amount;
            }
        }
    }

    private bool IsSavingsCategory(int catId) => _data.SavingsCategoryId == catId;

    /// <summary>
    /// Mirrors web's getCatPlanned/getCategoryPlanned (identical on both the
    /// Plan and Overview tabs): savings first, then allocations, then Fixed
    /// transactions due this period. A not-yet-due Fixed template is
    /// deliberately NOT a further fallback — it reports separately via
    /// NotDuePlannedTotal.
    /// </summary>
    private decimal PlannedTotal(int catId)
    {
        if (IsSavingsCategory(catId)) return _savingsTotal;
        if (_allocByCat.TryGetValue(catId, out var alloc) && alloc != 0) return alloc;
        return _fixedDueByCat.GetValueOrDefault(catId);
    }

    private decimal PlannedTotalForPerson(int catId, int personId)
    {
        if (IsSavingsCategory(catId)) return _savingsByPerson.GetValueOrDefault(personId);
        if (_allocByPersonCat.TryGetValue((catId, personId), out var alloc) && alloc != 0) return alloc;
        return _fixedDueByPersonCat.GetValueOrDefault((catId, personId));
    }

    /// <summary>
    /// Flags the variable transactions that pushed their category past its
    /// plan, walking each category chronologically and marking every
    /// transaction from the point the running total first crosses the plan
    /// onward — not every transaction in an over-budget category. The
    /// baseline is PlannedTotal, the same per-category figure this response
    /// already reports. Received transactions (negative amounts) never flag.
    /// </summary>
    private void ComputeOverBudget()
    {
        var byCat = new Dictionary<int, List<Transaction>>();
        foreach (var tx in _data.Transactions)
        {
            if (SpendFilter.IsFixed(tx) || tx.CategoryId is not { } catId) continue;
            if (!byCat.TryGetValue(catId, out var list)) byCat[catId] = list = new List<Transaction>();
            list.Add(tx);
        }

        foreach (var (catId, txs) in byCat)
        {
            var planned = PlannedTotal(catId);
            // Sorted for a stable answer: two transactions on the same day
            // have no inherent order, so ID breaks the tie the same way on
            // every call.
            txs.Sort((a, b) =>
            {
                var cmp = Nullable.Compare(a.Date, b.Date);
                return cmp != 0 ? cmp : a.Id.CompareTo(b.Id);
            });
            decimal running = 0;
            foreach (var tx in txs)
            {
                running += tx.Amount;
                if (running > planned && tx.Amount > 0) _overBudgetTxIds.Add(tx.Id);
            }
        }
        _overBudgetTxIds.Sort();
    }

    /// <summary>Both null when nothing is pending, so the wire stays empty for the common case.</summary>
    private (Money? Amount, DateOnly? NextDue) NotDueSummary(int catId)
    {
        var amount = _notDueFixedByCat.GetValueOrDefault(catId);
        if (amount == 0) return (null, null);
        _notDueNextDueByCat.TryGetValue(catId, out var due);
        return (Money.FromDecimal(amount), due == default ? null : due);
    }

    /// <summary>The union of every category ID any source references — the candidate set both BuildPlanCategories and BuildOverviewCategories filter down from.</summary>
    private List<int> AllCategoryIds()
    {
        var seen = new HashSet<int>();
        foreach (var id in _actualByCat.Keys) seen.Add(id);
        foreach (var id in _allocByCat.Keys) seen.Add(id);
        foreach (var id in _fixedDueByCat.Keys) seen.Add(id);
        foreach (var id in _notDueFixedByCat.Keys) seen.Add(id);
        if (_data.SavingsCategoryId is { } savingsCatId && _data.SavingsSources.Count > 0) seen.Add(savingsCatId);
        return seen.ToList();
    }

    private List<PersonExpenseSummaryDto> BuildPersonBreakdowns(int catId)
    {
        var result = new List<PersonExpenseSummaryDto>();
        foreach (var p in _data.People)
        {
            var planned = PlannedTotalForPerson(catId, p.Id);
            var actual = _actualByPersonCat.GetValueOrDefault((catId, p.Id));
            if (planned == 0 && actual == 0) continue;
            result.Add(new PersonExpenseSummaryDto(p.Id, Money.FromDecimal(planned), Money.FromDecimal(actual)));
        }
        return result;
    }

    /// <summary>
    /// The Expense Plan tab's category rows (visible if allocated, due this
    /// period, or templated regardless of due status; sorted by planned
    /// descending) plus total_committed. A not-yet-due template keeps its
    /// row visible but contributes a planned total of zero, carrying its
    /// amount in NotDuePlannedTotal instead — so total_committed is exactly
    /// the sum of the rows above it.
    /// </summary>
    private (List<CategoryExpenseSummaryDto> Categories, decimal TotalCommitted) BuildPlanCategories()
    {
        var categories = new List<CategoryExpenseSummaryDto>();
        decimal totalCommitted = 0;
        foreach (var catId in AllCategoryIds())
        {
            var hasSavingsSources = IsSavingsCategory(catId) && _data.SavingsSources.Count > 0;
            var visible = _catIdsWithAlloc.Contains(catId) || hasSavingsSources ||
                _fixedDueByCat.GetValueOrDefault(catId) != 0 || _fixedExpenseCatIds.Contains(catId);
            if (!visible) continue;

            var planned = PlannedTotal(catId);
            var (notDue, nextDue) = NotDueSummary(catId);
            categories.Add(new CategoryExpenseSummaryDto(
                catId, Money.FromDecimal(planned), null, false, BuildPersonBreakdowns(catId), notDue, nextDue));
            totalCommitted += planned;
        }
        categories.Sort((a, b) => b.PlannedTotal.ToDecimal().CompareTo(a.PlannedTotal.ToDecimal()));
        return (categories, totalCommitted);
    }

    /// <summary>
    /// The Expense Overview tab's category rows (visible if there's actual
    /// spend OR any plan; sorted by actual descending) plus total_planned,
    /// total_actual, total_over_budget, and total_unplanned. A category
    /// whose only obligation is a not-yet-due template is absent here — it
    /// has no actual spend and no plan, a 0/0 row on a tab about what was
    /// actually spent. It still appears on the Plan tab.
    /// </summary>
    private (List<CategoryExpenseSummaryDto> Categories, decimal TotalPlanned, decimal TotalActual, decimal TotalOverBudget, decimal TotalUnplanned) BuildOverviewCategories()
    {
        var categories = new List<CategoryExpenseSummaryDto>();
        decimal totalPlanned = 0, totalActual = _uncategorized, totalOverBudget = 0, totalUnplanned = _uncategorized;

        foreach (var catId in AllCategoryIds())
        {
            var hasSavingsSources = IsSavingsCategory(catId) && _data.SavingsSources.Count > 0;
            var visible = _actualByCat.GetValueOrDefault(catId) != 0 || _catIdsWithAlloc.Contains(catId) ||
                hasSavingsSources || _fixedDueByCat.GetValueOrDefault(catId) != 0;
            if (!visible) continue;

            var planned = PlannedTotal(catId);
            var actual = _actualByCat.GetValueOrDefault(catId);
            var isOver = planned > 0 && actual > planned;

            categories.Add(new CategoryExpenseSummaryDto(
                catId, Money.FromDecimal(planned), Money.FromDecimal(actual), isOver, BuildPersonBreakdowns(catId), null, null));

            totalPlanned += planned;
            totalActual += actual;
            if (planned <= 0) totalUnplanned += actual;
            else if (actual > planned) totalOverBudget += actual - planned;
        }
        categories.Sort((a, b) => b.ActualTotal!.Value.ToDecimal().CompareTo(a.ActualTotal!.Value.ToDecimal()));
        return (categories, totalPlanned, totalActual, totalOverBudget, totalUnplanned);
    }

    public GetExpenseSummaryDto Response()
    {
        var (planCategories, totalCommitted) = BuildPlanCategories();
        var (overviewCategories, totalPlanned, totalActual, totalOverBudget, totalUnplanned) = BuildOverviewCategories();

        var incomeFromSources = _data.IncomeSources.Sum(s => s.DefaultAmount);
        var incomeFromEntries = _data.IncomeEntries.Sum(e => e.Amount);

        return new GetExpenseSummaryDto(
            Money.FromDecimal(incomeFromSources), Money.FromDecimal(incomeFromEntries),
            Money.FromDecimal(totalCommitted), Money.FromDecimal(totalPlanned), Money.FromDecimal(totalActual),
            Money.FromDecimal(_uncategorized), Money.FromDecimal(totalOverBudget), Money.FromDecimal(totalUnplanned),
            Money.FromDecimal(incomeFromSources - totalCommitted), Money.FromDecimal(incomeFromEntries - totalActual),
            Money.FromDecimal(incomeFromEntries - totalPlanned),
            planCategories, overviewCategories,
            Money.FromDecimal(_fixedActual), Money.FromDecimal(_variableActual), _overBudgetTxIds);
    }
}
