using AutoMapper;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Changelog;

public sealed class ChangelogMappingProfile : Profile
{
    public ChangelogMappingProfile()
    {
        CreateMap<ChangelogItem, ChangelogItemDto>();
        CreateMap<ChangelogRelease, ChangelogReleaseDto>();
    }
}
