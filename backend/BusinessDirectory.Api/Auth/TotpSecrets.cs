using System.Security.Cryptography;

namespace BusinessDirectory.Api.Auth;

/// <summary>
/// Seals and opens a TOTP secret for storage in `admin_users.totp_secret_encrypted`.
///
/// A TOTP secret is a bearer credential: anyone holding it can produce valid codes forever. Hashing
/// is not an option, because the server has to reproduce the code, so it is encrypted instead with a
/// key held outside the database (`Auth:TotpEncryptionKey`, from Key Vault in production). A leak of
/// the database alone therefore does not defeat MFA.
///
/// AES-GCM gives confidentiality and integrity together, so a tampered row fails to open rather than
/// decrypting to rubbish. The stored layout is: nonce (12 bytes) | tag (16 bytes) | ciphertext.
/// </summary>
public sealed class TotpSecrets(byte[] key)
{
    private const int NonceBytes = 12;   // the size AES-GCM is specified for
    private const int TagBytes = 16;

    /// <summary>The key length AES-256-GCM needs. Checked when settings load, so a short key fails at startup.</summary>
    public const int KeyBytes = 32;

    private readonly byte[] _key = key.Length == KeyBytes
        ? key
        : throw new ArgumentException($"The TOTP encryption key must be {KeyBytes} bytes.", nameof(key));

    public byte[] Seal(byte[] secret)
    {
        var sealed_ = new byte[NonceBytes + TagBytes + secret.Length];
        var nonce = sealed_.AsSpan(0, NonceBytes);
        var tag = sealed_.AsSpan(NonceBytes, TagBytes);
        var ciphertext = sealed_.AsSpan(NonceBytes + TagBytes);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, secret, ciphertext, tag);
        return sealed_;
    }

    /// <summary>
    /// Returns null when the value cannot be opened — a wrong key, or a row that has been altered.
    /// The caller treats that as "this admin has no usable secret" rather than crashing, so one bad
    /// row cannot take the whole admin sign-in down.
    /// </summary>
    public byte[]? TryOpen(byte[]? sealedSecret)
    {
        if (sealedSecret is null || sealedSecret.Length <= NonceBytes + TagBytes)
        {
            return null;
        }

        var nonce = sealedSecret.AsSpan(0, NonceBytes);
        var tag = sealedSecret.AsSpan(NonceBytes, TagBytes);
        var ciphertext = sealedSecret.AsSpan(NonceBytes + TagBytes);
        var secret = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(_key, TagBytes);
            aes.Decrypt(nonce, ciphertext, tag, secret);
            return secret;
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }
    }
}
