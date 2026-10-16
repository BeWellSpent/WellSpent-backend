namespace WellSpent.Application.Abstractions;

/// <summary>bcrypt, cost 12 — same algorithm and cost as Go's golang.org/x/crypto/bcrypt, so password hashes already in the `users` table verify correctly from either backend.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
