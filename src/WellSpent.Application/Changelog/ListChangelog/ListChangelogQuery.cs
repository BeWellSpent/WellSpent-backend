using AutoMapper;
using MediatR;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Changelog.ListChangelog;

/// <summary>
/// Authenticated but not superuser-gated — the reader-facing call behind the
/// "what's new" prompt and the Help browser. Components empty means every
/// component.
/// </summary>
public sealed record ListChangelogQuery(IReadOnlyList<string> Components, int LimitPerComponent) : IRequest<ChangelogListResult>;

public sealed class ListChangelogQueryHandler(IChangelogRepository changelog, IMapper mapper)
    : IRequestHandler<ListChangelogQuery, ChangelogListResult>
{
    public async Task<ChangelogListResult> Handle(ListChangelogQuery request, CancellationToken ct)
    {
        foreach (var c in request.Components)
        {
            if (!ChangelogConstants.ValidComponents.Contains(c))
            {
                throw new AppValidationException($"unknown component: {c}");
            }
        }

        var limit = request.LimitPerComponent > 0 ? request.LimitPerComponent : ChangelogConstants.DefaultReleasesPerComponent;
        var releases = await changelog.ListReleasesWithItemsAsync(request.Components, ct);

        // The repository orders by component then releasedAt/createdAt DESC,
        // so truncating here is a running count per component rather than a
        // sort — mirrors the Go service doing this in application code
        // instead of a SQL window function, since it's a display cap, not a
        // query concern.
        var perComponent = new Dictionary<string, int>();
        var kept = new List<Domain.Entities.ChangelogRelease>();
        foreach (var r in releases)
        {
            var count = perComponent.GetValueOrDefault(r.Component);
            if (count >= limit)
            {
                continue;
            }
            perComponent[r.Component] = count + 1;
            kept.Add(r);
        }

        return new ChangelogListResult(mapper.Map<List<ChangelogReleaseDto>>(kept), ChangelogConstants.ServerVersion);
    }
}
