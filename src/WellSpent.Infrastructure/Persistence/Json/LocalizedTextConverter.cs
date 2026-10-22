using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WellSpent.Infrastructure.Persistence.Json;

/// <summary>Converts a locale-keyed Dictionary&lt;string,string&gt; to/from a jsonb column.</summary>
public static class LocalizedTextConverter
{
    public static readonly ValueConverter<Dictionary<string, string>, string> Instance = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new());

    public static readonly ValueComparer<Dictionary<string, string>> Comparer = new(
        (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
        v => v.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
        v => new Dictionary<string, string>(v));
}
