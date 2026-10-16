using MediatR;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.PaymentMethods.CreatePaymentMethod;

/// <summary>
/// Mirrors a real gap in Go, carried over rather than silently closed: the
/// only check is that BudgetPersonId is non-zero — there is no verification
/// that the caller is actually a member of that person's budget. Noted here
/// so it isn't mistaken for an oversight in this port specifically.
/// </summary>
public sealed record CreatePaymentMethodCommand(Guid UserId, string Name, string Type, int BudgetPersonId, string Color)
    : IRequest<PaymentMethodDto>;

public sealed class CreatePaymentMethodCommandHandler(ITransactionRepository transactions)
    : IRequestHandler<CreatePaymentMethodCommand, PaymentMethodDto>
{
    public async Task<PaymentMethodDto> Handle(CreatePaymentMethodCommand request, CancellationToken ct)
    {
        if (request.BudgetPersonId == 0)
        {
            throw new AppValidationException("budget_person_id is required");
        }

        var method = await transactions.CreatePaymentMethodAsync(new PaymentMethod
        {
            Name = request.Name,
            PaymentTypeId = PaymentTypeMapping.ToId(request.Type),
            UserId = request.UserId,
            BudgetPersonId = request.BudgetPersonId,
            Color = request.Color,
        }, ct);

        return PaymentMethodMapping.ToDto(method);
    }
}
