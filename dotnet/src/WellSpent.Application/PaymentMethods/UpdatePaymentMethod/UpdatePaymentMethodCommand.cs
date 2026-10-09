using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.PaymentMethods.UpdatePaymentMethod;

/// <summary>Name/color/alias only — PaymentType can't change after creation. Empty alias clears it.</summary>
public sealed record UpdatePaymentMethodCommand(Guid UserId, Guid Id, string Name, string Color, string Alias)
    : IRequest<PaymentMethodDto>;

public sealed class UpdatePaymentMethodCommandHandler(
    ITransactionRepository transactions, IBudgetProfileRepository profiles, BudgetAccessGuard access)
    : IRequestHandler<UpdatePaymentMethodCommand, PaymentMethodDto>
{
    public async Task<PaymentMethodDto> Handle(UpdatePaymentMethodCommand request, CancellationToken ct)
    {
        var method = await transactions.GetPaymentMethodAsync(request.Id, ct);
        if (method.BudgetPersonId is { } personId)
        {
            var person = await profiles.GetPersonByIdAsync(personId, ct);
            await access.EnsureCollaboratorOrAboveAsync(person.BudgetProfileId, request.UserId, ct);
        }
        else if (method.UserId != request.UserId)
        {
            throw new ForbiddenException("access denied");
        }

        var alias = request.Alias == "" ? null : request.Alias;
        var updated = await transactions.UpdatePaymentMethodAsync(request.Id, request.Name, request.Color, alias, ct);
        return PaymentMethodMapping.ToDto(updated);
    }
}
