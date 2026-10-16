using AutoMapper;
using MediatR;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Status.GetActiveStatusBanner;

/// <summary>Public — no auth required. Null means nothing is live, which is the normal state, not an error.</summary>
public sealed record GetActiveStatusBannerQuery : IRequest<StatusBannerDto?>;

public sealed class GetActiveStatusBannerQueryHandler(IStatusBannerRepository banners, IMapper mapper)
    : IRequestHandler<GetActiveStatusBannerQuery, StatusBannerDto?>
{
    public async Task<StatusBannerDto?> Handle(GetActiveStatusBannerQuery request, CancellationToken ct)
    {
        try
        {
            var banner = await banners.GetActiveAsync(ct);
            return mapper.Map<StatusBannerDto>(banner);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}
