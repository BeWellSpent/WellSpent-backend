namespace WellSpent.Application.Plaid;

/// <summary>An imported transaction's category key and id from Plaid's classification; empty when it has none.</summary>
public static class PlaidSyncCategoryResolution
{
    public static string ResolveKey(string name, string pfcPrimary, string pfcDetailed) =>
        PlaidCategoryMapping.Resolve(pfcPrimary, pfcDetailed);

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
