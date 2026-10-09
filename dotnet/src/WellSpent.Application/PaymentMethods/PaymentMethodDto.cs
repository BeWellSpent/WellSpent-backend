using WellSpent.Domain.Entities;

namespace WellSpent.Application.PaymentMethods;

public sealed record PaymentMethodDto(Guid Id, string Name, string Type, long BudgetPersonId, string Color, string Alias);

/// <summary>Plain-string wire convention for payment_type_id (matches this rewrite's role/cycle/chart-type convention), keyed on the seeded payment_type order (migration 000001) rather than Go's proto enum numbering — sidesteps a real Go bug where "Other" (db id 8) never matched proto PAYMENT_TYPE_OTHER (99) under Go's direct int-to-enum cast.</summary>
public static class PaymentTypeMapping
{
    private static readonly string[] Names = ["unspecified", "cash", "credit", "debit", "digital_wallet", "bank_transfer", "crypto", "investment", "other"];

    public static string ToName(int? paymentTypeId) =>
        paymentTypeId is { } id && id >= 0 && id < Names.Length ? Names[id] : "unspecified";

    public static int ToId(string name) => Array.IndexOf(Names, name) is var i && i >= 0 ? i : 0;
}

public static class PaymentMethodMapping
{
    public static PaymentMethodDto ToDto(PaymentMethod m) => new(
        m.Id, m.Name, PaymentTypeMapping.ToName(m.PaymentTypeId), m.BudgetPersonId ?? 0, m.Color, m.Alias ?? "");
}
