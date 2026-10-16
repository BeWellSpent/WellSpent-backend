using MediatR;
using WellSpent.Application.Abstractions;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Auth.Login;

public sealed record LoginCommand(string Email, string Password, bool RememberMe) : IRequest<LoginResult>;

public sealed record LoginResult(string AccessToken, long ExpiresIn, string Language, string Currency);

public sealed class LoginCommandHandler(IUserRepository users, IPasswordHasher passwordHasher, IJwtService jwt)
    : IRequestHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken ct)
    {
        Domain.Entities.User user;
        try
        {
            user = await users.GetByEmailAsync(request.Email, ct);
        }
        catch (NotFoundException)
        {
            // Surfaced as a generic error to avoid email enumeration.
            throw new AppValidationException("invalid email or password");
        }

        if (!user.IsActive)
        {
            throw user.Status == "disabled"
                ? new ForbiddenException("account is deactivated — contact support to recover your account")
                : new ForbiddenException("account is inactive");
        }
        if (user.HashedPassword is null)
        {
            throw new AppValidationException("account uses OAuth login only");
        }
        if (!passwordHasher.Verify(request.Password, user.HashedPassword))
        {
            throw new AppValidationException("invalid email or password");
        }

        var lifetime = request.RememberMe ? Auth.AuthConstants.RememberMeTokenLifetime : Auth.AuthConstants.DefaultTokenLifetime;
        var token = jwt.GenerateToken(user.Id, lifetime);
        return new LoginResult(token, (long)lifetime.TotalSeconds, user.Language, user.Currency);
    }
}
