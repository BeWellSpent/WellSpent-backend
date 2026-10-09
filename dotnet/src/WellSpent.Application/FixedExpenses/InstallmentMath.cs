namespace WellSpent.Application.FixedExpenses;

/// <summary>
/// Pure port of installment_plan.go. Go computes on big.Int specifically
/// because a NUMERIC round-trips through a lossy float64 intermediate; C#'s
/// decimal is already exact, so Math.Round with AwayFromZero (round-half-
/// away-from-zero, matching Go's divRoundHalf) does the same job in one step.
/// </summary>
public static class InstallmentMath
{
    /// <summary>Splits a purchase evenly across n payments, rounded to the nearest cent. The division is exact and rounds once — scaling to cents and dividing twice would compound the error.</summary>
    public static decimal InstallmentAmount(decimal total, int n) =>
        n < 1 ? 0 : Math.Round(total / n, 2, MidpointRounding.AwayFromZero);

    /// <summary>The date of the last payment: first payment plus one month per remaining payment. Deliberately month-based regardless of the budget's own cycle.</summary>
    public static DateOnly InstallmentEndDate(DateOnly firstPayment, int totalPayments) =>
        firstPayment.AddMonths(totalPayments - 1);
}
