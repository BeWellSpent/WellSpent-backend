using MediatR;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.PaymentMethods.ListPaymentMethods;

/// <summary>No membership check — mirrors Go, which only requires an authenticated caller, not a member of this specific budget.</summary>
public sealed record ListPaymentMethodsQuery(Guid UserId, Guid BudgetProfileId) : IRequest<List<PaymentMethodDto>>;

public sealed class ListPaymentMethodsQueryHandler(ITransactionRepository transactions)
    : IRequestHandler<ListPaymentMethodsQuery, List<PaymentMethodDto>>
{
    public async Task<List<PaymentMethodDto>> Handle(ListPaymentMethodsQuery request, CancellationToken ct)
    {
        var rows = await transactions.ListPaymentMethodsAsync(request.BudgetProfileId, ct);
        return rows.Select(PaymentMethodMapping.ToDto).ToList();
    }
}
