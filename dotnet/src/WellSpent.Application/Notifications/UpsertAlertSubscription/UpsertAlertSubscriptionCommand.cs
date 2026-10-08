using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Notifications.UpsertAlertSubscription;

public sealed record UpsertAlertSubscriptionCommand(
    Guid UserId, Guid BudgetProfileId, string AlertType, string Channel,
    decimal ThresholdPct, string ThresholdScope, int? CategoryId, bool NotifyAllMembers)
    : IRequest<AlertSubscriptionDto>;

public sealed class UpsertAlertSubscriptionCommandHandler(
    INotificationRepository notifications, IUserRepository users, BudgetAccessGuard access, IMapper mapper)
    : IRequestHandler<UpsertAlertSubscriptionCommand, AlertSubscriptionDto>
{
    public async Task<AlertSubscriptionDto> Handle(UpsertAlertSubscriptionCommand request, CancellationToken ct)
    {
        // Caller must be a member of the budget. A non-member's
        // NotFoundException propagates as-is (404) — mirrors Go exactly;
        // this check, unlike Invite's admin checks, does not collapse to 403.
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);

        // Free tier: new_transaction alerts and more than 2 subscriptions per budget require Pro.
        var caller = await users.GetByIdAsync(request.UserId, ct);
        if (caller.Plan == "free")
        {
            if (request.AlertType == NotificationConstants.AlertTypeNewTransaction)
            {
                throw new AppValidationException("free tier: new_transaction alerts require a Pro subscription");
            }
            var existing = await notifications.ListSubscriptionsAsync(request.UserId, request.BudgetProfileId, ct);
            if (existing.Count >= NotificationConstants.FreeTierMaxSubscriptionsPerBudget &&
                !existing.Any(s => s.AlertType == request.AlertType))
            {
                throw new AppValidationException("free tier: budget alerts are limited to 2; upgrade to Pro for unlimited");
            }
        }

        var subscription = new AlertSubscription
        {
            UserId = request.UserId,
            BudgetProfileId = request.BudgetProfileId,
            AlertType = request.AlertType,
            Channel = request.Channel,
            ThresholdPct = request.AlertType == NotificationConstants.AlertTypeSpendingThreshold ? request.ThresholdPct : null,
            ThresholdScope = string.IsNullOrEmpty(request.ThresholdScope) ? null : request.ThresholdScope,
            CategoryId = request.CategoryId,
            NotifyAllMembers = request.NotifyAllMembers,
        };

        var saved = await notifications.UpsertSubscriptionAsync(subscription, ct);
        return mapper.Map<AlertSubscriptionDto>(saved);
    }
}
