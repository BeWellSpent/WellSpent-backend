using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Status.CreateStatusBanner;

/// <summary>StartsAt null means "now" — the case that matters when something is on fire.</summary>
public sealed record CreateStatusBannerCommand(
    Guid UserId, string Severity, Dictionary<string, string> Message, DateTime? StartsAt, DateTime EndsAt)
    : IRequest<StatusBannerDto>;

public sealed class CreateStatusBannerCommandHandler(
    IStatusBannerRepository banners, SuperuserGuard superuser, IMapper mapper)
    : IRequestHandler<CreateStatusBannerCommand, StatusBannerDto>
{
    public async Task<StatusBannerDto> Handle(CreateStatusBannerCommand request, CancellationToken ct)
    {
        await superuser.EnsureSuperuserAsync(request.UserId, ct);

        var severity = request.Severity.Trim().ToLowerInvariant();
        if (!StatusBannerConstants.ValidSeverities.Contains(severity))
        {
            throw new AppValidationException("severity must be one of: info, warning, critical");
        }

        var message = LocalizedTextValidator.ValidateAndTrim(request.Message, StatusBannerConstants.MaxMessageLength, "message");

        var startsAt = request.StartsAt?.ToUniversalTime() ?? DateTime.UtcNow;
        if (request.EndsAt == default)
        {
            throw new AppValidationException("ends_at is required");
        }
        var endsAt = request.EndsAt.ToUniversalTime();
        if (endsAt <= startsAt)
        {
            throw new AppValidationException("ends_at must be after starts_at");
        }
        // A banner whose window has already closed would be written and never
        // seen — almost certainly a timezone mistake under pressure, so it's
        // rejected rather than silently accepted.
        if (endsAt <= DateTime.UtcNow)
        {
            throw new AppValidationException("ends_at must be in the future");
        }

        var created = await banners.CreateAsync(new StatusBanner
        {
            Severity = severity,
            Message = message,
            StartsAt = startsAt,
            EndsAt = endsAt,
            CreatedBy = request.UserId,
        }, ct);

        return mapper.Map<StatusBannerDto>(created);
    }
}
