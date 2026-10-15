using MediatR;

namespace WellSpent.Application.Auth.RefreshToken;

public sealed record RefreshTokenCommand(string Token) : IRequest<RefreshTokenResult>;

public sealed record RefreshTokenResult(string AccessToken, long ExpiresIn);

/// <summary>Not implemented on the Go side either (returns connect.CodeUnimplemented) — mirrored exactly rather than invented, so this sub-issue doesn't silently grow new behavior.</summary>
public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, RefreshTokenResult>
{
    public Task<RefreshTokenResult> Handle(RefreshTokenCommand request, CancellationToken ct) =>
        throw new NotImplementedException();
}
