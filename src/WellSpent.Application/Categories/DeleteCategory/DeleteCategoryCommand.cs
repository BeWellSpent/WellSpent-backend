using MediatR;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Categories.DeleteCategory;

public sealed record DeleteCategoryCommand(Guid UserId, int Id, int ReplacementId) : IRequest;

public sealed class DeleteCategoryCommandHandler(ITransactionRepository transactions)
    : IRequestHandler<DeleteCategoryCommand>
{
    public async Task Handle(DeleteCategoryCommand request, CancellationToken ct)
    {
        var category = await transactions.GetCategoryAsync(request.Id, ct);
        if (category.IsSystem)
        {
            throw new ForbiddenException("system categories cannot be deleted");
        }
        if (category.UserId != request.UserId)
        {
            throw new ForbiddenException("access denied");
        }

        var replacement = await transactions.GetCategoryAsync(request.ReplacementId, ct);
        if (replacement.UserId is { } replacementOwnerId && replacementOwnerId != request.UserId)
        {
            throw new ForbiddenException("replacement category is not accessible");
        }

        await transactions.DeleteCategoryAndReassignAsync(request.Id, request.UserId, request.ReplacementId, ct);
    }
}
