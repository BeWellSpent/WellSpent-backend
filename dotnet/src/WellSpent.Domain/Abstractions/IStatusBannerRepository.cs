using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

public interface IStatusBannerRepository
{
    /// <summary>The single banner clients should show. Throws <see cref="Exceptions.NotFoundException"/> when nothing is live — the common case, which callers must treat as ordinary, not exceptional.</summary>
    Task<StatusBanner> GetActiveAsync(CancellationToken ct);

    Task<StatusBanner> CreateAsync(StatusBanner banner, CancellationToken ct);

    /// <summary>The operator's full history, including expired rows — not the client-facing view.</summary>
    Task<List<StatusBanner>> ListAsync(int limit, CancellationToken ct);

    Task<StatusBanner> ExpireAsync(Guid id, CancellationToken ct);
}
