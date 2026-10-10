namespace WellSpent.Application.Plaid;

/// <summary>
/// Mirrors internal/plaid/category.go's ResolvePlaidCategory exactly. Returns
/// the category.system_key string (see ITransactionRepository.ListSystemCategoriesAsync)
/// for a Plaid personal_finance_category pair — detailed is checked first,
/// falling back to primary. Empty string only for a primary outside Plaid's
/// published taxonomy, which should never happen since every one of Plaid's
/// 16 primaries is covered below.
/// </summary>
public static class PlaidCategoryMapping
{
    private static readonly Dictionary<string, string> DetailedToCategory = new()
    {
        ["FOOD_AND_DRINK_GROCERIES"] = "groceries",
        ["GENERAL_MERCHANDISE_PET_SUPPLIES"] = "pet",
        ["MEDICAL_VETERINARY_SERVICES"] = "pet",
        ["PERSONAL_CARE_LAUNDRY_AND_DRY_CLEANING"] = "services",
        ["GENERAL_SERVICES_INSURANCE"] = "insurance",
        ["GENERAL_SERVICES_AUTOMOTIVE"] = "auto",
        ["GENERAL_SERVICES_CHILDCARE"] = "baby",
        ["TRANSPORTATION_GAS"] = "gas",
        ["TRANSPORTATION_TOLLS"] = "gas",
        ["RENT_AND_UTILITIES_RENT"] = "rent",
        ["TRANSFER_IN_SAVINGS"] = "savings",
        ["TRANSFER_OUT_SAVINGS"] = "savings",
        ["ENTERTAINMENT_TV_AND_MOVIES"] = "subscription",
        ["LOAN_PAYMENTS_CREDIT_CARD_PAYMENT"] = "payment",
    };

    private static readonly Dictionary<string, string> PrimaryToCategory = new()
    {
        ["FOOD_AND_DRINK"] = "eating_out",
        ["GENERAL_MERCHANDISE"] = "shopping",
        ["HOME_IMPROVEMENT"] = "house",
        ["MEDICAL"] = "wellness",
        ["PERSONAL_CARE"] = "wellness",
        ["GENERAL_SERVICES"] = "services",
        ["TRANSPORTATION"] = "transportation",
        ["TRAVEL"] = "travel",
        ["RENT_AND_UTILITIES"] = "utilities",
        ["ENTERTAINMENT"] = "entertainment",
        ["BANK_FEES"] = "misc",
        ["GOVERNMENT_AND_NON_PROFIT"] = "misc",
        ["LOAN_PAYMENTS"] = "loan",
        ["INCOME"] = "income",
        ["TRANSFER_IN"] = "transfer",
        ["TRANSFER_OUT"] = "transfer",
    };

    public static string Resolve(string? primary, string? detailed)
    {
        if (detailed is not null && DetailedToCategory.TryGetValue(detailed, out var detailedKey))
        {
            return detailedKey;
        }

        return primary is not null && PrimaryToCategory.TryGetValue(primary, out var primaryKey) ? primaryKey : "";
    }
}
