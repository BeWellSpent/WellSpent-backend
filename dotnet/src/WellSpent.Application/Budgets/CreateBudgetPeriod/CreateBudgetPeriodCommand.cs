using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.CreateBudgetPeriod;

/// <summary>
/// Admin-only. See BudgetPeriodRollover's doc comment for what this
/// deliberately does not do yet (income pre-fill, fixed-expense spawn,
/// carryover — all later B5 batches).
/// </summary>
public sealed record CreateBudgetPeriodCommand(Guid UserId, Guid BudgetProfileId) : IRequest<BudgetPeriodDto>;

public sealed class CreateBudgetPeriodCommandHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<CreateBudgetPeriodCommand, BudgetPeriodDto>
{
    public async Task<BudgetPeriodDto> Handle(CreateBudgetPeriodCommand request, CancellationToken ct)
    {
        var profile = await access.EnsureAdminAsync(request.BudgetProfileId, request.UserId, ct);
        var period = await BudgetPeriodRollover.CreateNextPeriodAsync(profiles, profile, ct);
        return mapper.Map<BudgetPeriodDto>(period);
    }
}
