using AutoMapper;
using MediatR;
using WellSpent.Application.Abstractions;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Users.UpdateMe;

/// <summary>
/// Full-replace semantics, mirroring the Go handler exactly: FirstName/
/// LastName/CountryCode/StateCode are cleared to NULL when sent empty (not
/// left unchanged), while FilingStatus/TaxPaymentFrequency/Language/Currency
/// are always written as given, even "" or 0 — this is a full profile
/// replace, not a partial patch.
/// </summary>
public sealed record UpdateMeCommand(
    Guid UserId,
    string FirstName,
    string LastName,
    string CountryCode,
    string StateCode,
    int FilingStatus,
    int TaxPaymentFrequency,
    string Language,
    string Currency) : IRequest<UserDto>;

public sealed class UpdateMeCommandHandler(IUserRepository users, IMapper mapper) : IRequestHandler<UpdateMeCommand, UserDto>
{
    public async Task<UserDto> Handle(UpdateMeCommand request, CancellationToken ct)
    {
        var updated = await users.UpdateAsync(
            request.UserId,
            firstName: string.IsNullOrEmpty(request.FirstName) ? null : request.FirstName,
            lastName: string.IsNullOrEmpty(request.LastName) ? null : request.LastName,
            countryCode: string.IsNullOrEmpty(request.CountryCode) ? null : request.CountryCode,
            stateCode: string.IsNullOrEmpty(request.StateCode) ? null : request.StateCode,
            filingStatus: request.FilingStatus.ToString(),
            taxPaymentFrequency: request.TaxPaymentFrequency,
            language: request.Language,
            currency: request.Currency,
            ct);

        return mapper.Map<UserDto>(updated);
    }
}
