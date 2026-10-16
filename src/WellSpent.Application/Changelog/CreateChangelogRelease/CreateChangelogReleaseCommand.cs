using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Changelog.CreateChangelogRelease;

public sealed record CreateChangelogItemInput(string ChangeType, string SummaryEn, string SummaryEs);

/// <summary>ReleasedAt null means "now" — publishing normally happens at release time.</summary>
public sealed record CreateChangelogReleaseCommand(
    Guid UserId, string Component, string Version, DateTime? ReleasedAt, List<CreateChangelogItemInput> Items)
    : IRequest<ChangelogReleaseDto>;

/// <summary>
/// Publishes one release and all its items as one atomic save (EF Core wraps
/// a single SaveChanges in an implicit transaction) — see
/// IChangelogRepository.CreateReleaseAsync's doc comment for how this differs
/// from Go's unwrapped per-item loop.
/// </summary>
public sealed class CreateChangelogReleaseCommandHandler(
    Domain.Abstractions.IChangelogRepository changelog, SuperuserGuard superuser, IMapper mapper)
    : IRequestHandler<CreateChangelogReleaseCommand, ChangelogReleaseDto>
{
    public async Task<ChangelogReleaseDto> Handle(CreateChangelogReleaseCommand request, CancellationToken ct)
    {
        await superuser.EnsureSuperuserAsync(request.UserId, ct);

        if (!ChangelogConstants.ValidComponents.Contains(request.Component))
        {
            throw new AppValidationException("must be one of web, ios, server");
        }

        var version = request.Version.Trim();
        if (version.Length == 0)
        {
            throw new AppValidationException("version is required");
        }
        if (version.EnumerateRunes().Count() > ChangelogConstants.MaxVersionLength)
        {
            throw new AppValidationException("version is too long");
        }

        // A release with nothing to say should not be published — it would
        // show a reader an empty "what's new", which is worse than nothing.
        if (request.Items.Count == 0)
        {
            throw new AppValidationException("at least one item is required");
        }

        var items = new List<ChangelogItem>(request.Items.Count);
        for (var i = 0; i < request.Items.Count; i++)
        {
            var changeType = request.Items[i].ChangeType;
            var summaryEn = request.Items[i].SummaryEn.Trim();
            var summaryEs = request.Items[i].SummaryEs.Trim();
            if (!ChangelogConstants.ValidChangeTypes.Contains(changeType))
            {
                throw new AppValidationException("must be one of added, fixed, changed");
            }
            if (summaryEn.Length == 0)
            {
                throw new AppValidationException("English summary is required");
            }
            if (summaryEn.EnumerateRunes().Count() > ChangelogConstants.MaxSummaryLength ||
                summaryEs.EnumerateRunes().Count() > ChangelogConstants.MaxSummaryLength)
            {
                throw new AppValidationException("summary is too long");
            }
            items.Add(new ChangelogItem { ChangeType = changeType, SummaryEn = summaryEn, SummaryEs = summaryEs, Position = i });
        }

        var release = new ChangelogRelease
        {
            Component = request.Component,
            Version = version,
            ReleasedAt = request.ReleasedAt?.ToUniversalTime() ?? DateTime.UtcNow,
            CreatedBy = request.UserId,
            Items = items,
        };

        // The repository maps the unique (component, version) violation to a
        // DuplicateException — republishing a version has to fail loudly, not
        // list it twice.
        var created = await changelog.CreateReleaseAsync(release, ct);
        return mapper.Map<ChangelogReleaseDto>(created);
    }
}
