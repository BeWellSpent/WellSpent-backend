using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.DeleteBudgetProfile;

public sealed record DeleteBudgetProfileCommand(Guid UserId, Guid Id) : IRequest;

/// <summary>
/// Two steps rather than one, in this order, because payment methods are the
/// one thing reachable from a budget that does not carry a budget_profile_id
/// of its own — they are user-scoped and belong to the budget only through
/// budget_person_id. So they are read *before* the delete (that link is the
/// only way to find them, and it is about to be cut) and removed *after* it
/// (transaction.payment_method_id and savings_source.payment_method_id have
/// no ON DELETE, so those rows have to go with the profile first). Mirrors
/// Go's BudgetProfileService.Delete exactly (see budget-creation-flow.md).
/// </summary>
public sealed class DeleteBudgetProfileCommandHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles)
    : IRequestHandler<DeleteBudgetProfileCommand>
{
    public async Task Handle(DeleteBudgetProfileCommand request, CancellationToken ct)
    {
        await access.EnsureAdminAsync(request.Id, request.UserId, ct);

        // Best-effort: a failure here must not block the delete the user
        // asked for. The worst case is an unreachable row, not a budget that
        // refuses to go away.
        List<Guid> paymentMethodIds = [];
        var listFailed = false;
        try
        {
            paymentMethodIds = await profiles.ListPaymentMethodIdsByBudgetProfileAsync(request.Id, ct);
        }
        catch
        {
            listFailed = true;
        }

        await profiles.DeleteAsync(request.Id, ct);

        if (!listFailed && paymentMethodIds.Count > 0)
        {
            try
            {
                await profiles.DeletePaymentMethodsByIdsAsync(paymentMethodIds, ct);
            }
            catch
            {
                // Unreachable rows, not a failed delete — matches Go.
            }
        }
    }
}
