using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Budgets;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.DeleteSavingsSource;

public sealed record DeleteSavingsSourceCommand(Guid UserId, int Id, Guid BudgetProfileId) : IRequest;

public sealed class DeleteSavingsSourceCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, ITransactionRepository transactions,
    ILogger<DeleteSavingsSourceCommandHandler> logger)
    : IRequestHandler<DeleteSavingsSourceCommand>
{
    public async Task Handle(DeleteSavingsSourceCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);

        var source = await profiles.GetSavingsSourceAsync(request.Id, request.BudgetProfileId, ct);

        if (source.PaymentMethodId is not null)
        {
            await SavingsTransactionSpawning.DeleteExistingTransactionsAsync(transactions, logger, request.BudgetProfileId, source, ct);
        }

        await profiles.DeleteSavingsSourceAsync(request.Id, request.BudgetProfileId, ct);
    }
}
