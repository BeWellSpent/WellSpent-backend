using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.CreateBudgetPeriod;

/// <summary>
/// Admin-only. See BudgetPeriodRollover's doc comment for what this
/// deliberately still does not do (fixed-expense spawn, savings-source
/// transaction spawn, carryover — later B5 batches).
/// </summary>
public sealed record CreateBudgetPeriodCommand(Guid UserId, Guid BudgetProfileId) : IRequest<BudgetPeriodDto>;

public sealed class CreateBudgetPeriodCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, TaxReserveRecalculator taxReserve,
    IMapper mapper, ILogger<CreateBudgetPeriodCommandHandler> logger)
    : IRequestHandler<CreateBudgetPeriodCommand, BudgetPeriodDto>
{
    public async Task<BudgetPeriodDto> Handle(CreateBudgetPeriodCommand request, CancellationToken ct)
    {
        var profile = await access.EnsureAdminAsync(request.BudgetProfileId, request.UserId, ct);
        var period = await BudgetPeriodRollover.CreateNextPeriodAsync(profiles, taxReserve, logger, profile, ct);
        return mapper.Map<BudgetPeriodDto>(period);
    }
}
