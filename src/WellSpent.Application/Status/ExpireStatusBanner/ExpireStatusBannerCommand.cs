using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Status.ExpireStatusBanner;

/// <summary>Takes a banner down ahead of schedule by moving ends_at to now. Rows are never deleted — what was announced, and when, is worth keeping.</summary>
public sealed record ExpireStatusBannerCommand(Guid UserId, Guid BannerId) : IRequest<StatusBannerDto>;

public sealed class ExpireStatusBannerCommandHandler(
    IStatusBannerRepository banners, SuperuserGuard superuser, IMapper mapper)
    : IRequestHandler<ExpireStatusBannerCommand, StatusBannerDto>
{
    public async Task<StatusBannerDto> Handle(ExpireStatusBannerCommand request, CancellationToken ct)
    {
        await superuser.EnsureSuperuserAsync(request.UserId, ct);
        var expired = await banners.ExpireAsync(request.BannerId, ct);
        return mapper.Map<StatusBannerDto>(expired);
    }
}
