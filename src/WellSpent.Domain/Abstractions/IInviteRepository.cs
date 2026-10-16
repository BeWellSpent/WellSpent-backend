using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

public interface IInviteRepository
{
    Task<BudgetInvite> CreateAsync(BudgetInvite invite, CancellationToken ct);
    Task<BudgetInvite> GetByTokenAsync(Guid token, CancellationToken ct);
    Task<BudgetInvite> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<BudgetInvite>> ListByProfileAsync(Guid profileId, CancellationToken ct);
    Task<BudgetInvite> UpdateStatusAsync(Guid id, string status, CancellationToken ct);
}
