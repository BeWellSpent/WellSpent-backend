using MediatR;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Categories.CreateCategory;

public sealed record CreateCategoryCommand(Guid UserId, string Name, string Color) : IRequest<CategoryDto>;

public sealed class CreateCategoryCommandHandler(ITransactionRepository transactions)
    : IRequestHandler<CreateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(CreateCategoryCommand request, CancellationToken ct)
    {
        var category = await transactions.CreateCategoryAsync(request.Name, request.UserId, request.Color, ct);
        return CategoryMapping.ToDto(category);
    }
}
