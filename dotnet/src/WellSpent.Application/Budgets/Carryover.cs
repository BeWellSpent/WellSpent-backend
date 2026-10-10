using System.Numerics;
using WellSpent.Application.ExpenseSummary;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Budgets;

/// <summary>One transaction to create in the next period, keyed by system-category key — resolved to an id by the caller.</summary>
public sealed record CarryoverRow(decimal Amount, string CategoryKey, Guid? PaymentMethodId);

/// <summary>Pure port of carryover.go — closing balance into Savings/Debt rows for the next period. No I/O.</summary>
public static class Carryover
{
    public const string SavingsSystemKey = "savings";
    public const string DebtSystemKey = "debt";

    // NUMERIC(15,4) has 4 decimal digits, so every amount here is an exact multiple of this unit.
    private const long Scale = 10_000;
    private const long ScalePerCent = Scale / 100;

    private sealed record Bucket(Guid? MethodId, long SpendScaled);

    /// <summary>Reduces a closed period's transactions/income to the three numbers Compute needs, via the same spend filter the Expense Summary uses — so the carried balance matches what the user was shown.</summary>
    public static (decimal Remainder, Dictionary<Guid, decimal> SpendByMethod, decimal UnattributedSpend) Inputs(
        IEnumerable<Transaction> transactions, IEnumerable<IncomeEntry> incomeEntries, HashSet<int> nonSpendCategoryIds)
    {
        var spendByMethod = new Dictionary<Guid, decimal>();
        decimal totalSpend = 0, unattributed = 0;
        foreach (var tx in transactions)
        {
            if (SpendFilter.IsNonSpendTransaction(tx, nonSpendCategoryIds) || SpendFilter.IsUnpaidFixed(tx)) continue;
            totalSpend += tx.Amount;
            if (tx.PaymentMethodId is not { } methodId) { unattributed += tx.Amount; continue; }
            spendByMethod[methodId] = spendByMethod.GetValueOrDefault(methodId) + tx.Amount;
        }
        return (incomeEntries.Sum(e => e.Amount) - totalSpend, spendByMethod, unattributed);
    }

    /// <summary>Empty when the period ended exactly even.</summary>
    public static List<CarryoverRow> Compute(decimal remainder, Dictionary<Guid, decimal> spendByMethod, decimal unattributedSpend)
    {
        if (remainder == 0) return [];
        if (remainder > 0) return [new CarryoverRow(remainder, SavingsSystemKey, null)];

        var shortfall = -remainder;
        var (buckets, totalSpendScaled) = BuildBuckets(spendByMethod, unattributedSpend);

        // A shortfall with no positive spend behind it can't be attributed — one unattributed row beats silently dropping the balance.
        if (totalSpendScaled <= 0) return [new CarryoverRow(shortfall, DebtSystemKey, null)];

        var shares = Apportion(ToScaled(shortfall), buckets, totalSpendScaled);
        var rows = new List<CarryoverRow>(buckets.Count);
        for (var i = 0; i < buckets.Count; i++)
        {
            if (shares[i] == 0) continue;
            rows.Add(new CarryoverRow(FromScaled(shares[i]), DebtSystemKey, buckets[i].MethodId));
        }
        return rows;
    }

    /// <summary>Highest spend first; only positive net spend qualifies. Ties put the unattributed bucket last — a named method is the more useful place for a rounding cent.</summary>
    private static (List<Bucket> Buckets, long TotalSpendScaled) BuildBuckets(Dictionary<Guid, decimal> spendByMethod, decimal unattributedSpend)
    {
        var buckets = new List<Bucket>();
        long total = 0;
        foreach (var (id, spend) in spendByMethod)
        {
            if (spend <= 0) continue;
            var scaled = ToScaled(spend);
            buckets.Add(new Bucket(id, scaled));
            total += scaled;
        }
        if (unattributedSpend > 0)
        {
            var scaled = ToScaled(unattributedSpend);
            buckets.Add(new Bucket(null, scaled));
            total += scaled;
        }
        buckets.Sort((a, b) => b.SpendScaled != a.SpendScaled ? b.SpendScaled.CompareTo(a.SpendScaled) : SortKey(a).CompareTo(SortKey(b)));
        return (buckets, total);
    }

    private static string SortKey(Bucket b) => b.MethodId?.ToString() ?? "￿";

    /// <summary>Largest-remainder method, floored to whole cents via exact BigInteger division so shares sum to `amountScaled` exactly. Any leftover sub-cent remainder lands on the highest-spend bucket.</summary>
    private static long[] Apportion(long amountScaled, List<Bucket> buckets, long totalSpendScaled)
    {
        var totalCents = amountScaled / ScalePerCent;
        var subCentScaled = amountScaled % ScalePerCent;

        var shares = new long[buckets.Count];
        var order = new (int Idx, BigInteger Rem, long Spend)[buckets.Count];
        long assigned = 0;

        for (var i = 0; i < buckets.Count; i++)
        {
            var quo = BigInteger.DivRem((BigInteger)totalCents * buckets[i].SpendScaled, totalSpendScaled, out var rem);
            shares[i] = (long)quo;
            order[i] = (i, rem, buckets[i].SpendScaled);
            assigned += shares[i];
        }

        // Largest lost fraction first; ties go to the bigger spender, then the earlier (already spend-sorted) bucket.
        Array.Sort(order, (a, b) => b.Rem != a.Rem ? b.Rem.CompareTo(a.Rem) : b.Spend != a.Spend ? b.Spend.CompareTo(a.Spend) : a.Idx.CompareTo(b.Idx));
        for (var k = 0L; k < totalCents - assigned; k++)
        {
            shares[order[(int)(k % order.Length)].Idx]++;
        }

        for (var i = 0; i < shares.Length; i++) shares[i] *= ScalePerCent;
        if (subCentScaled != 0 && shares.Length > 0) shares[0] += subCentScaled;
        return shares;
    }

    private static long ToScaled(decimal amount) => (long)(amount * Scale);
    private static decimal FromScaled(long scaled) => scaled / (decimal)Scale;
}
