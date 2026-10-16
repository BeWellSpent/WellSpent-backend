using AutoMapper;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Users;

public sealed class UserMappingProfile : Profile
{
    public UserMappingProfile()
    {
        // UserDto is an immutable positional record; ConstructUsing is explicit
        // about building it via the constructor rather than relying on
        // AutoMapper's member-mapping convention finding (or not finding) a
        // way to populate init-only properties with no parameterless ctor.
        CreateMap<User, UserDto>()
            .ConstructUsing(s => new UserDto(
                s.Id,
                s.Email,
                s.FirstName,
                s.LastName,
                s.IsActive,
                UserDisplayRules.IsVerificationSatisfied(s),
                s.CreatedAt,
                s.CountryCode,
                s.StateCode,
                UserDisplayRules.ParseFilingStatus(s.FilingStatus),
                s.TaxPaymentFrequency,
                s.Language,
                s.Currency,
                s.Plan,
                UserDisplayRules.IsApplePrivateEmail(s.Email)))
            // Without this, AutoMapper still runs its convention-based
            // member-by-member pass after ConstructUsing and overwrites the
            // custom-computed IsVerified/FilingStatus/HasApplePrivateEmail
            // with a raw same-name property copy from User — silently
            // discarding exactly the derivation this mapping exists for.
            .ForAllMembers(opt => opt.Ignore());
    }
}
