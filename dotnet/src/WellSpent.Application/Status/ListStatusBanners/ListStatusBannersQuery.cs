using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Status.ListStatusBanners;

public sealed record ListStatusBannersQuery(Guid UserId, int Limit) : IRequest<List<StatusBannerDto>>;

public sealed class ListStatusBannersQueryHandler(
    IStatusBannerRepository banners, SuperuserGuard superuser, IMapper mapper)
    : IRequestHandler<ListStatusBannersQuery, List<StatusBannerDto>>
{
    public async Task<List<StatusBannerDto>> Handle(ListStatusBannersQuery request, CancellationToken ct)
    {
        await superuser.EnsureSuperuserAsync(request.UserId, ct);

        var limit = request.Limit > 0 ? request.Limit : StatusBannerConstants.DefaultListLimit;
        var rows = await banners.ListAsync(limit, ct);
        return mapper.Map<List<StatusBannerDto>>(rows);
    }
}
