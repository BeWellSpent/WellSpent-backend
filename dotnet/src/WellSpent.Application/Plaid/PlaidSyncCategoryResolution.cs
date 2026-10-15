namespace WellSpent.Application.Plaid;

/// <summary>Mirrors internal/service/plaid_sync.go's syncResolveCategory/syncResolveCategoryID exactly.</summary>
public static class PlaidSyncCategoryResolution
{
    /// <summary>"payroll" in the name is a fallback override when Plaid returns no PFC data.</summary>
    public static string ResolveKey(string name, string pfcPrimary, string pfcDetailed) =>
        name.Contains("payroll", StringComparison.OrdinalIgnoreCase)
            ? "income"
            : PlaidCategoryMapping.Resolve(pfcPrimary, pfcDetailed);

    /// <summary>A non-empty key with a null id means no matching system category — the transaction still imports, just uncategorized.</summary>
    public static (string CategoryKey, int? CategoryId) ResolveId(
        string name, string pfcPrimary, string pfcDetailed, Dictionary<string, int> systemCategories)
    {
        var key = ResolveKey(name, pfcPrimary, pfcDetailed);
        if (key.Length == 0)
        {
            return ("", null);
        }

        return systemCategories.TryGetValue(key, out var id) ? (key, id) : (key, null);
    }
}
