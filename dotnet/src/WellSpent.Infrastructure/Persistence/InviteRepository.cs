using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Infrastructure.Persistence;

public sealed class InviteRepository(WellSpentDbContext db) : IInviteRepository
{
    public async Task<BudgetInvite> CreateAsync(BudgetInvite invite, CancellationToken ct)
    {
        db.BudgetInvites.Add(invite);
        await db.SaveChangesAsync(ct);
        return invite;
    }

    public async Task<BudgetInvite> GetByTokenAsync(Guid token, CancellationToken ct) =>
        await db.BudgetInvites.FirstOrDefaultAsync(i => i.Token == token, ct)
            ?? throw new NotFoundException("invite", token.ToString());

    public async Task<BudgetInvite> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.BudgetInvites.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException("invite", id.ToString());

    public Task<List<BudgetInvite>> ListByProfileAsync(Guid profileId, CancellationToken ct) =>
        db.BudgetInvites
            .Where(i => i.BudgetProfileId == profileId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

    public async Task<BudgetInvite> UpdateStatusAsync(Guid id, string status, CancellationToken ct)
    {
        var invite = await GetByIdAsync(id, ct);
        invite.Status = status;
        await db.SaveChangesAsync(ct);
        return invite;
    }
}
