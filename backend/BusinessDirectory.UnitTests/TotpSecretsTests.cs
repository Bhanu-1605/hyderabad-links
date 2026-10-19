using System.Security.Cryptography;
using BusinessDirectory.Api.Auth;

namespace BusinessDirectory.UnitTests;

public class TotpSecretsTests
{
    private static TotpSecrets NewSecrets() => new(RandomNumberGenerator.GetBytes(TotpSecrets.KeyBytes));

    [Fact]
    public void A_sealed_secret_opens_back_to_itself()
    {
        var secrets = NewSecrets();
        var secret = Totp.NewSecret();

        Assert.Equal(secret, secrets.TryOpen(secrets.Seal(secret)));
    }

    [Fact]
    public void The_sealed_form_does_not_contain_the_secret()
    {
        var secrets = NewSecrets();
        var secret = Totp.NewSecret();
        var sealedSecret = secrets.Seal(secret);

        // The ciphertext must not be the plaintext sitting behind a header.
        Assert.DoesNotContain(Convert.ToHexString(secret), Convert.ToHexString(sealedSecret));
    }

    [Fact]
    public void Sealing_the_same_secret_twice_gives_different_bytes()
    {
        var secrets = NewSecrets();
        var secret = Totp.NewSecret();

        // A fresh nonce each time, so equal secrets are not recognisable as equal in the database.
        Assert.NotEqual(secrets.Seal(secret), secrets.Seal(secret));
    }

    [Fact]
    public void Another_key_cannot_open_it()
    {
        var secret = Totp.NewSecret();
        var sealedSecret = NewSecrets().Seal(secret);

        Assert.Null(NewSecrets().TryOpen(sealedSecret));
    }

    [Fact]
    public void A_tampered_value_fails_to_open_rather_than_returning_rubbish()
    {
        var secrets = NewSecrets();
        var sealedSecret = secrets.Seal(Totp.NewSecret());
        sealedSecret[^1] ^= 0xFF;   // flip a bit in the ciphertext

        Assert.Null(secrets.TryOpen(sealedSecret));
    }

    [Fact]
    public void A_tampered_nonce_also_fails()
    {
        var secrets = NewSecrets();
        var sealedSecret = secrets.Seal(Totp.NewSecret());
        sealedSecret[0] ^= 0xFF;

        Assert.Null(secrets.TryOpen(sealedSecret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(27)]   // shorter than nonce + tag
    [InlineData(28)]   // exactly nonce + tag, so no ciphertext
    public void Values_too_short_to_be_sealed_secrets_return_null(int? length) =>
        Assert.Null(NewSecrets().TryOpen(length is null ? null : new byte[length.Value]));

    [Fact]
    public void A_key_of_the_wrong_length_is_refused_at_construction()
    {
        // Caught when settings load, so a short key stops the app rather than failing per request.
        Assert.Throws<ArgumentException>(() => new TotpSecrets(new byte[16]));
    }
}

public class RecoveryCodesTests
{
    [Fact]
    public void A_full_set_is_generated_and_every_code_differs()
    {
        var codes = RecoveryCodes.Generate();

        Assert.Equal(RecoveryCodes.CodeCount, codes.Count);
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Two_sets_do_not_overlap()
    {
        var first = RecoveryCodes.Generate();
        var second = RecoveryCodes.Generate();

        Assert.Empty(first.Intersect(second));
    }

    [Theory]
    [InlineData("ABCDEFGH-IJKLMNOP")]
    [InlineData("abcdefgh-ijklmnop")]
    [InlineData("ABCDEFGH IJKLMNOP")]
    [InlineData("ABCDEFGHIJKLMNOP")]
    [InlineData("  abcdefgh - ijklmnop  ")]
    public void However_it_is_typed_it_hashes_the_same(string typed)
    {
        // Spaces, case and the hyphen are all noise: the admin is reading off paper.
        Assert.Equal(RecoveryCodes.Hash("ABCDEFGH-IJKLMNOP"), RecoveryCodes.Hash(typed));
    }

    [Fact]
    public void A_different_code_hashes_differently() =>
        Assert.NotEqual(RecoveryCodes.Hash("ABCDEFGH-IJKLMNOP"), RecoveryCodes.Hash("ABCDEFGH-IJKLMNOQ"));

    [Fact]
    public void Normalising_keeps_only_letters_and_digits() =>
        Assert.Equal("ABC123", RecoveryCodes.Normalize(" a-b c/1.2_3 "));

    [Fact]
    public void Generated_codes_are_shaped_as_two_groups()
    {
        Assert.All(RecoveryCodes.Generate(), code =>
        {
            var parts = code.Split('-');
            Assert.Equal(2, parts.Length);
            Assert.All(parts, part => Assert.Equal(8, part.Length));
        });
    }
}
