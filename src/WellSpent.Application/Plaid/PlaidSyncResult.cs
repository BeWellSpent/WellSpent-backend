namespace WellSpent.Application.Plaid;

/// <summary>How many transactions one connected account contributed in a sync run. Sorted by count desc then name, so a run's output is deterministic rather than depending on dictionary iteration order.</summary>
public sealed record AccountImport(string Account, int Count);

/// <summary>The outcome of syncing one connection. Mirrors Go's ItemSyncResult exactly.</summary>
public sealed class ItemSyncResult
{
    public required Guid ItemId { get; init; }
    public string InstitutionName { get; init; } = "";
    public int Imported { get; set; }
    public int AutoConfirmed { get; set; }
    public int Queued { get; set; }
    public int SkippedNoPeriod { get; set; }
    public int SkippedDuplicate { get; set; }
    public int Modified { get; set; }
    public int Removed { get; set; }

    /// <summary>A pending transaction Plaid settled under a new id, repointed onto the existing local row instead of being deleted and reimported as a fresh, unlinked duplicate.</summary>
    public int Repointed { get; set; }

    /// <summary>Only transactions that ended up newly available to the user — auto-confirmed and queued-for-review ones are reported through their own counters, so counting them here too would double-notify.</summary>
    public List<AccountImport> ByAccount { get; set; } = [];

    /// <summary>Set when the connection's owner is on the free plan and the sync was skipped. A reported outcome, never a silent no-op.</summary>
    public bool SkippedUnentitled { get; set; }

    public Exception? Error { get; set; }
}

/// <summary>Groups the connections of one budget profile's sync run.</summary>
public sealed class ProfileSyncResult
{
    public required Guid ProfileId { get; init; }
    public List<ItemSyncResult> Items { get; set; } = [];
}
