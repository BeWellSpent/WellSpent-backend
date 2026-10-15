using MediatR;
using WellSpent.Application.Abstractions;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Users.ChangePassword;

public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword) : IRequest<Unit>;

public sealed class ChangePasswordCommandHandler(IUserRepository users, IPasswordHasher passwordHasher)
    : IRequestHandler<ChangePasswordCommand, Unit>
{
    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(request.UserId, ct);
        if (user.HashedPassword is null)
        {
            throw new AppValidationException("account uses OAuth login only");
        }
        if (!passwordHasher.Verify(request.CurrentPassword, user.HashedPassword))
        {
            throw new AppValidationException("current password is incorrect");
        }

        var hashed = passwordHasher.Hash(request.NewPassword);
        await users.UpdatePasswordAsync(request.UserId, hashed, ct);
        return Unit.Value;
    }
}
