using System.Security.Cryptography;
using WellSpent.Application.Abstractions;

namespace WellSpent.Infrastructure.Security;

/// <summary>
/// AES-256-GCM, mirroring internal/crypto/aes.go byte-for-byte: a hex-encoded
/// 32-byte key, output is base64(nonce ++ ciphertext+tag) using the GCM
/// standard 12-byte nonce and 16-byte tag — a value either backend encrypts
/// decrypts correctly on the other.
/// </summary>
public sealed class AesCryptoService : ICryptoService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string Encrypt(string plaintext, string hexKey)
    {
        var key = DecodeKey(hexKey);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Go's cipher.GCM.Seal appends the tag to the ciphertext before the
        // base64 step, producing nonce ++ ciphertext ++ tag as one blob.
        var sealed_ = new byte[NonceSize + cipherBytes.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, sealed_, 0, NonceSize);
        Buffer.BlockCopy(cipherBytes, 0, sealed_, NonceSize, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, sealed_, NonceSize + cipherBytes.Length, TagSize);

        return Convert.ToBase64String(sealed_);
    }

    public string Decrypt(string encoded, string hexKey)
    {
        var key = DecodeKey(hexKey);
        var sealed_ = Convert.FromBase64String(encoded);
        if (sealed_.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("crypto: ciphertext too short");
        }

        var nonce = sealed_[..NonceSize];
        var tag = sealed_[^TagSize..];
        var cipherBytes = sealed_[NonceSize..^TagSize];
        var plainBytes = new byte[cipherBytes.Length];

        using var gcm = new AesGcm(key, TagSize);
        gcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] DecodeKey(string hexKey)
    {
        if (hexKey.Length != 64)
        {
            throw new ArgumentException("crypto: key must be a 64-char hex string (32 bytes)", nameof(hexKey));
        }
        return Convert.FromHexString(hexKey);
    }
}
