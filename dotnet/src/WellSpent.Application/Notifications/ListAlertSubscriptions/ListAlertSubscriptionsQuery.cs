using AutoMapper;
using MediatR;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Notifications.ListAlertSubscriptions;

/// <summary>No membership check — scoped by (user_id, budget_profile_id) in the query itself, so a non-member just gets an empty list.</summary>
public sealed record ListAlertSubscriptionsQuery(Guid UserId, Guid BudgetProfileId) : IRequest<List<AlertSubscriptionDto>>;

public sealed class ListAlertSubscriptionsQueryHandler(INotificationRepository notifications, IMapper mapper)
    : IRequestHandler<ListAlertSubscriptionsQuery, List<AlertSubscriptionDto>>
{
    public async Task<List<AlertSubscriptionDto>> Handle(ListAlertSubscriptionsQuery request, CancellationToken ct)
    {
        var subs = await notifications.ListSubscriptionsAsync(request.UserId, request.BudgetProfileId, ct);
        return mapper.Map<List<AlertSubscriptionDto>>(subs);
    }
}
