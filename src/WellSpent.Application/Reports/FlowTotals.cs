using WellSpent.Application.Common;
using WellSpent.Application.ExpenseSummary;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Reports;

public sealed record FlowTotalsDto(
    Money MoneyIn, Money MoneyOut, Money Income, Money Refunds, Money TransfersIn, Money Spent, Money TransfersOut, Money PaidToCards);

/// <summary>A period's money by flow, as positive magnitudes (the Reports "money in · out · moved" card).</summary>
public static class FlowTotals
{
    /// <summary>
    /// Skips excluded rows and unpaid fixed bills, like every total. A card payment shows on both
    /// accounts when both are linked, so it counts once: the larger of its outgoing and incoming legs.
    /// </summary>
    public static FlowTotalsDto Compute(IEnumerable<Transaction> transactions)
    {
        decimal income = 0, refunds = 0, transfersIn = 0, spent = 0, transfersOut = 0, paymentsOut = 0, paymentsIn = 0;
        foreach (var tx in transactions)
        {
            if (tx.IsExcluded || SpendFilter.IsUnpaidFixed(tx)) continue;
            var amount = tx.Amount;
            switch (tx.Flow ?? TransactionFlow.BySign(amount))
            {
                case TransactionFlow.Income: income -= amount; break;
                case TransactionFlow.Refund: refunds -= amount; break;
                case TransactionFlow.Spend: spent += amount; break;
                case TransactionFlow.Transfer when amount < 0: transfersIn -= amount; break;
                case TransactionFlow.Transfer: transfersOut += amount; break;
                case TransactionFlow.Payment when amount < 0: paymentsIn -= amount; break;
                case TransactionFlow.Payment: paymentsOut += amount; break;
            }
        }
        static decimal P(decimal v) => Math.Max(0, v);
        static Money M(decimal v) => Money.FromDecimal(v);
        // Card payments stay out of moneyOut: the purchases they settle already counted.
        return new FlowTotalsDto(
            M(P(income) + P(refunds) + P(transfersIn)), M(P(spent) + P(transfersOut)),
            M(P(income)), M(P(refunds)), M(P(transfersIn)), M(P(spent)), M(P(transfersOut)), M(P(Math.Max(paymentsOut, paymentsIn))));
    }
}
