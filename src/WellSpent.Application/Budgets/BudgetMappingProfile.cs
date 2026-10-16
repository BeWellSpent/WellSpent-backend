using AutoMapper;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Budgets;

public sealed class BudgetMappingProfile : Profile
{
    public BudgetMappingProfile()
    {
        CreateMap<BudgetProfile, BudgetProfileDto>();
        CreateMap<BudgetPeriod, BudgetPeriodDto>();
        CreateMap<BudgetPerson, BudgetPersonDto>();
    }
}
