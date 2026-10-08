using MediatR;

namespace WellSpent.Application.Auth.Logout;

/// <summary>JWT is stateless; invalidation is handled client-side by discarding the token — mirrors the Go handler exactly.</summary>
public sealed record LogoutCommand : IRequest<Unit>;

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    public Task<Unit> Handle(LogoutCommand request, CancellationToken ct) => Task.FromResult(Unit.Value);
}
