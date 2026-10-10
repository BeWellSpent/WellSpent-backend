using WellSpent.Domain.Entities;

namespace WellSpent.Application.TransactionReviews;

/// <summary>
/// Pure scoring core shared by manual-match review (this batch) and, once
/// ported, the Plaid sync job (B6) — mirrors Go's scoreBestMatch/
/// syncAmountWithinTolerance/syncNameWordsOverlap in plaid_sync.go exactly.
/// Weights: amount within $3 = 40, name match (alias or word-overlap) = 20,
/// payment method match = 20, category match = 20 — so ≥80 needs amount plus
/// two of the other three, or all three non-amount signals together.
/// </summary>
public static class TransactionMatching
{
    private const decimal AmountTolerance = 3.0m;

    public static (double Score, FixedExpense? Match) ScoreBestMatch(
        string name, decimal amount, int? categoryId, Guid? paymentMethodId,
        List<FixedExpense> expenses, Dictionary<Guid, List<string>> aliasesByFixedExpenseId)
    {
        var best = 0.0;
        FixedExpense? bestFe = null;
        var nameLower = name.ToLowerInvariant();

        foreach (var fe in expenses)
        {
            // A card installment settles inside the card's own balance and
            // never lands on a bank feed as its own line item, so anything
            // scoring against one is a false positive by construction
            // (issue #54). Skipped here, not at each call site, so neither
            // can forget.
            if (fe.IsInstallmentPlan) continue;

            var score = 0.0;
            if (AmountWithinTolerance(amount, fe)) score += 40;

            var aliasHit = aliasesByFixedExpenseId.TryGetValue(fe.Id, out var aliases) &&
                aliases.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase) || NameWordsOverlap(alias.ToLowerInvariant(), nameLower));
            // Exact match first: a short name ("F1") has no words >= 4 chars,
            // so word-overlap alone would score it 0 even against an identical name.
            if (aliasHit || string.Equals(name, fe.Name, StringComparison.OrdinalIgnoreCase) || NameWordsOverlap(nameLower, fe.Name.ToLowerInvariant()))
            {
                score += 20;
            }
            if (paymentMethodId is { } pmId && fe.PaymentMethodId == pmId) score += 20;
            if (categoryId is { } catId && fe.CategoryId == catId) score += 20;

            if (score > best)
            {
                best = score;
                bestFe = fe;
            }
        }

        return (best, bestFe);
    }

    private static bool AmountWithinTolerance(decimal txAmount, FixedExpense fe) =>
        Math.Abs(txAmount - fe.PlannedAmount) <= AmountTolerance;

    private static bool NameWordsOverlap(string a, string b)
    {
        var aWords = Words(a);
        return Words(b).Any(aWords.Contains);
    }

    private static HashSet<string> Words(string s)
    {
        var words = new HashSet<string>();
        var current = new System.Text.StringBuilder();
        foreach (var c in s)
        {
            if (c is >= 'a' and <= 'z')
            {
                current.Append(c);
            }
            else
            {
                Flush();
            }
        }
        Flush();
        return words;

        void Flush()
        {
            if (current.Length >= 4) words.Add(current.ToString());
            current.Clear();
        }
    }
}
