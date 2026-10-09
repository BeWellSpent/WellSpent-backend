using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Budgets.RemoveBudgetPerson;

public sealed record RemoveBudgetPersonCommand(
    Guid UserId, Guid BudgetProfileId, int PersonId, int ReplacementPersonId, Guid? ReplacementPaymentMethodId)
    : IRequest;

public sealed class RemoveBudgetPersonCommandHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles)
    : IRequestHandler<RemoveBudgetPersonCommand>
{
    public async Task Handle(RemoveBudgetPersonCommand request, CancellationToken ct)
    {
        var profile = await access.EnsureAdminAsync(request.BudgetProfileId, request.UserId, ct);
        var person = await profiles.GetPersonAsync(request.PersonId, request.BudgetProfileId, ct);

        // Protect the profile owner from removal.
        if (person.UserId is { } pid && pid == profile.UserId)
        {
            throw new AppValidationException("budget owner cannot be removed");
        }

        if (request.ReplacementPersonId == 0)
        {
            await profiles.SoftRemovePersonAsync(request.PersonId, request.BudgetProfileId, ct);
            return;
        }

        // Throws NotFoundException if the replacement doesn't belong to this profile.
        await profiles.GetPersonAsync(request.ReplacementPersonId, request.BudgetProfileId, ct);

        await profiles.SoftRemovePersonAndReassignAsync(
            request.PersonId, request.BudgetProfileId,
            request.ReplacementPaymentMethodId ?? Guid.Empty, request.ReplacementPersonId, ct);
    }
}
