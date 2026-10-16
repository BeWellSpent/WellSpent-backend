using AutoMapper;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Status;

public sealed class StatusBannerMappingProfile : Profile
{
    public StatusBannerMappingProfile()
    {
        CreateMap<StatusBanner, StatusBannerDto>();
    }
}
