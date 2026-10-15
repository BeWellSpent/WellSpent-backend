namespace WellSpent.Application.Common;

/// <summary>
/// The wire shape for money, locked by the epic's own planning doc: structured
/// {units, nanos, currency} rather than a plain decimal, so existing client
/// money-parsing code (written against proto Money) needs no changes across
/// the cutover. Unlike Go's moneyFromNumeric/numericFromMoney — which need
/// exact big.Int arithmetic because a NUMERIC round-trips through a lossy
/// float64 intermediate — C#'s decimal is already an exact base-10 type, so
/// no such workaround is needed here.
/// </summary>
public readonly record struct Money(long Units, int Nanos, string Currency = "USD")
{
    public static Money FromDecimal(decimal amount, string currency = "USD")
    {
        var units = (long)decimal.Truncate(amount);
        var nanos = (int)((amount - units) * 1_000_000_000m);
        return new Money(units, nanos, currency);
    }

    public decimal ToDecimal() => Units + Nanos / 1_000_000_000m;
}
