namespace WellSpent.Application.Plaid;

/// <summary>Mirrors internal/plaid/account.go exactly.</summary>
public static class PlaidAccountMapping
{
    /// <summary>Maps a Plaid account type/subtype to a payment_type lookup id (Cash=1, Credit=2, Debit=3, Digital Wallet=4, Bank Transfer=5, Crypto=6, Investment=7).</summary>
    public static int PaymentTypeId(string? accountType, string? accountSubtype) => accountType?.ToLowerInvariant() switch
    {
        "credit" => 2,
        "investment" or "brokerage" => 7,
        "depository" => string.Equals(accountSubtype, "savings", StringComparison.OrdinalIgnoreCase) ? 5 : 3,
        _ => 5,
    };

    public static string AccountName(string name, string? mask) =>
        string.IsNullOrEmpty(mask) ? name : $"{name} ···{mask}";
}
