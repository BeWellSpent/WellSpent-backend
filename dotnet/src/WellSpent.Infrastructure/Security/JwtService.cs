using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WellSpent.Application.Abstractions;
using WellSpent.Infrastructure.Configuration;

namespace WellSpent.Infrastructure.Security;

/// <summary>Mirrors internal/auth/jwt.go's JWTService.GenerateTokenWithLifetime exactly: HS256, a bare "sub" claim plus standard exp/iat — no issuer or audience, since the Go side sets none either.</summary>
public sealed class JwtService(AppConfig config) : IJwtService
{
    public string GenerateToken(Guid userId, TimeSpan lifetime)
    {
        var handler = new JsonWebTokenHandler();
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["sub"] = userId.ToString() },
            IssuedAt = now,
            Expires = now.Add(lifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.JwtSecret)),
                SecurityAlgorithms.HmacSha256),
        };
        return handler.CreateToken(descriptor);
    }
}
