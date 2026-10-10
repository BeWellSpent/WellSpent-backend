using WellSpent.Domain.Entities;

namespace WellSpent.Application.Plaid;

/// <summary>
/// Mirrors internal/service/plaid_sync.go's syncScoreBestMatch exactly —
/// deliberately a separate function from TransactionReviews.TransactionMatching
/// (the manual-match/ConfirmTransactionReview scorer, ported in B5 batch 7),
/// not a reuse of it, because Go's two scorers are not identical: this one
/// does NOT skip IsInstallmentPlan fixed expenses, while the manual-path one
/// does. That earlier batch's doc comment claimed this one would share the
/// same core "once B6 ports the Plaid sync job" — re-reading plaid_sync.go
/// for this batch found that claim wrong, so it is corrected here rather than
/// carried forward as a silent behavioral change. Also returns whether the
/// best match hit via an alias/exact-amount (bestAliasHit/bestAmountOk),
/// which the sync engine's auto-confirm-vs-queue branch needs and the
/// manual path's single ≥80 threshold does not.
/// </summary>
public static class PlaidSyncMatching
{
    private const decimal AmountTolerance = 3.0m;

    public static (double Score, FixedExpense? Match, bool AliasHit, bool AmountOk) ScoreBestMatch(
        string name, decimal amount, int? categoryId, Guid? paymentMethodId,
        List<FixedExpense> expenses, Dictionary<Guid, List<string>> aliasesByFixedExpenseId)
    {
        var best = 0.0;
        FixedExpense? bestFe = null;
        var bestAliasHit = false;
        var bestAmountOk = false;
        var nameLower = name.ToLowerInvariant();

        foreach (var fe in expenses)
        {
            var score = 0.0;

            var amountOk = AmountWithinTolerance(amount, fe);
            if (amountOk)
            {
                score += 40;
            }

            var aliasHit = aliasesByFixedExpenseId.TryGetValue(fe.Id, out var aliases) &&
                aliases.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase) || NameWordsOverlap(alias.ToLowerInvariant(), nameLower));

            // Exact match first: a short name ("F1") has no words >= 4 chars,
            // so word-overlap alone would score it 0 even against an identical name.
            if (aliasHit || string.Equals(name, fe.Name, StringComparison.OrdinalIgnoreCase) || NameWordsOverlap(nameLower, fe.Name.ToLowerInvariant()))
            {
                score += 20;
            }

            if (paymentMethodId is { } pmId && fe.PaymentMethodId == pmId)
            {
                score += 20;
            }

            if (categoryId is { } catId && fe.CategoryId == catId)
            {
                score += 20;
            }

            if (score > best)
            {
                best = score;
                bestFe = fe;
                bestAliasHit = aliasHit;
                bestAmountOk = amountOk;
            }
        }

        return (best, bestFe, bestAliasHit, bestAmountOk);
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
