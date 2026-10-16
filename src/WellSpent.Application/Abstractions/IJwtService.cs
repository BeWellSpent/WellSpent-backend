namespace WellSpent.Application.Abstractions;

/// <summary>Mirrors internal/auth/jwt.go's JWTService.GenerateTokenWithLifetime — same HS256 scheme, same "sub" claim, so a token either backend issues validates on both during the strangler-fig cutover.</summary>
public interface IJwtService
{
    string GenerateToken(Guid userId, TimeSpan lifetime);
}
