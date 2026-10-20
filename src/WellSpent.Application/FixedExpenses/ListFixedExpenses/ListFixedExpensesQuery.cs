using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.FixedExpenses.ListFixedExpenses;

public sealed record ListFixedExpensesQuery(Guid UserId, Guid BudgetProfileId, bool FocusedView = false) : IRequest<List<FixedExpenseDto>>;

public sealed class ListFixedExpensesQueryHandler(BudgetAccessGuard access, IFixedExpenseRepository fixedExpenses, IBudgetProfileRepository profiles)
    : IRequestHandler<ListFixedExpensesQuery, List<FixedExpenseDto>>
{
    public async Task<List<FixedExpenseDto>> Handle(ListFixedExpensesQuery request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);

        int? focusedPersonId = null;
        if (request.FocusedView)
        {
            BudgetPerson person;
            try
            {
                person = await profiles.GetPersonByUserIdAsync(request.BudgetProfileId, request.UserId, ct);
            }
            catch (NotFoundException)
            {
                throw new ForbiddenException("access denied");
            }
            focusedPersonId = person.Id;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await fixedExpenses.ListAsync(request.BudgetProfileId, ct, focusedPersonId);
        var dtos = new List<FixedExpenseDto>(rows.Count);
        foreach (var fe in rows)
        {
            var amountPaid = await FixedExpensePayoff.AmountPaidAsync(fixedExpenses, fe, ct);
            dtos.Add(FixedExpenseMapping.ToDto(fe, today, amountPaid));
        }
        return dtos;
    }
}
