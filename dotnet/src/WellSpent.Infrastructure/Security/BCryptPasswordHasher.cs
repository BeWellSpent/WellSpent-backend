using WellSpent.Application.Abstractions;

namespace WellSpent.Infrastructure.Security;

/// <summary>bcrypt, cost 12 — matches Go's golang.org/x/crypto/bcrypt exactly (standard algorithm, interoperable hash format), so existing/new hashes verify correctly from either backend during the strangler-fig cutover.</summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int Cost = 12;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, Cost);

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}
