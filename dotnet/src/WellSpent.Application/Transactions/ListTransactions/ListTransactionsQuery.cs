using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Transactions.ListTransactions;

public sealed record ListTransactionsQuery(
    Guid UserId, Guid BudgetPeriodId, int? CategoryId, string? TransactionType, bool FocusedView)
    : IRequest<List<TransactionDto>>;

public sealed class ListTransactionsQueryHandler(BudgetAccessGuard access, ITransactionRepository transactions, IBudgetProfileRepository profiles)
    : IRequestHandler<ListTransactionsQuery, List<TransactionDto>>
{
    public async Task<List<TransactionDto>> Handle(ListTransactionsQuery request, CancellationToken ct)
    {
        var period = await access.EnsureMemberOfPeriodAsync(request.BudgetPeriodId, request.UserId, ct);

        int? focusedPersonId = null;
        if (request.FocusedView)
        {
            BudgetPerson person;
            try
            {
                person = await profiles.GetPersonByUserIdAsync(period.BudgetProfileId, request.UserId, ct);
            }
            catch (NotFoundException)
            {
                throw new ForbiddenException("access denied");
            }
            focusedPersonId = person.Id;
        }

        var rows = await transactions.ListTransactionsAsync(
            request.BudgetPeriodId, request.CategoryId, TransactionTypeMapping.ToId(request.TransactionType), focusedPersonId, ct);
        return rows.Select(TransactionMapping.ToDto).ToList();
    }
}
