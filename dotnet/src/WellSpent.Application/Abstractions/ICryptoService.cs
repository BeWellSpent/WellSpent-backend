namespace WellSpent.Application.Abstractions;

/// <summary>AES-256-GCM, mirroring internal/crypto/aes.go exactly (hex key, base64 nonce+ciphertext) — values this backend encrypts must decrypt on the Go side and vice versa while both exist.</summary>
public interface ICryptoService
{
    string Encrypt(string plaintext, string hexKey);
    string Decrypt(string encoded, string hexKey);
}
