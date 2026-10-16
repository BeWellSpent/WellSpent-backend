using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Common;

/// <summary>
/// Mirrors the assertSuperuser method Go's StatusBannerService and
/// ChangelogService each define separately — shared here since both
/// domains' write handlers need the identical check. Same message
/// regardless of why access was denied, so this can't be used to probe
/// which accounts are privileged.
/// </summary>
public sealed class SuperuserGuard(IUserRepository users)
{
    public async Task EnsureSuperuserAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (!user.IsSuperuser)
        {
            throw new ForbiddenException("access denied");
        }
    }
}
