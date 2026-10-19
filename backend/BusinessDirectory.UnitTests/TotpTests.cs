using System.Text;
using BusinessDirectory.Api.Auth;

namespace BusinessDirectory.UnitTests;

public class TotpTests
{
    /// <summary>
    /// The published RFC 6238 test vectors for an HMAC-SHA1 secret of "12345678901234567890".
    /// They are the reason to trust the arithmetic: these values come from the specification, not
    /// from this implementation agreeing with itself. The RFC prints 8 digits; we use the low 6.
    /// </summary>
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Matches_the_RFC_6238_test_vectors(long unixSeconds, string expected)
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        var step = unixSeconds / Totp.StepSeconds;

        Assert.Equal(expected, Totp.Compute(secret, step));
    }

    [Fact]
    public void A_code_for_this_moment_is_accepted()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;

        Assert.True(Totp.TryVerify(secret, Totp.Compute(secret, Totp.StepAt(now)), now, lastUsedStep: null, out var step));
        Assert.Equal(Totp.StepAt(now), step);
    }

    [Theory]
    [InlineData(-1)]   // the phone is up to 30 seconds behind
    [InlineData(0)]
    [InlineData(1)]    // or up to 30 seconds ahead
    public void Codes_inside_the_drift_window_are_accepted(int offset)
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var code = Totp.Compute(secret, Totp.StepAt(now) + offset);

        Assert.True(Totp.TryVerify(secret, code, now, lastUsedStep: null, out var matched));
        Assert.Equal(Totp.StepAt(now) + offset, matched);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    [InlineData(100)]
    public void Codes_outside_the_window_are_refused(int offset)
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var code = Totp.Compute(secret, Totp.StepAt(now) + offset);

        Assert.False(Totp.TryVerify(secret, code, now, lastUsedStep: null, out _));
    }

    [Fact]
    public void A_code_cannot_be_used_twice()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var step = Totp.StepAt(now);
        var code = Totp.Compute(secret, step);

        Assert.True(Totp.TryVerify(secret, code, now, lastUsedStep: null, out var matched));
        Assert.False(Totp.TryVerify(secret, code, now, lastUsedStep: matched, out _));
    }

    [Fact]
    public void A_code_from_the_next_step_is_still_refused_once_spent()
    {
        // The case that made the stored step necessary: a code accepted from the *next* step belongs
        // to a later step than "now", so a spent-step check based on the clock would let it through.
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var nextStep = Totp.StepAt(now) + 1;
        var code = Totp.Compute(secret, nextStep);

        Assert.True(Totp.TryVerify(secret, code, now, lastUsedStep: null, out var matched));
        Assert.Equal(nextStep, matched);

        Assert.False(Totp.TryVerify(secret, code, now, lastUsedStep: matched, out _));
        // And the clock-derived step would have been too low to stop it:
        Assert.True(Totp.StepAt(now) < matched);
    }

    [Fact]
    public void Older_steps_are_refused_once_a_later_one_is_spent()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var previous = Totp.Compute(secret, Totp.StepAt(now) - 1);

        Assert.False(Totp.TryVerify(secret, previous, now, lastUsedStep: Totp.StepAt(now), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]      // too short
    [InlineData("1234567")]    // too long
    [InlineData("12345a")]     // not all digits
    [InlineData("  1234")]
    public void Malformed_codes_are_refused(string? code) =>
        Assert.False(Totp.TryVerify(Totp.NewSecret(), code, DateTimeOffset.UtcNow, lastUsedStep: null, out _));

    [Fact]
    public void A_wrong_code_of_the_right_shape_is_refused()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;
        var right = Totp.Compute(secret, Totp.StepAt(now));
        // Shift one digit, keeping the length and all-digits shape.
        var wrong = (right[0] == '0' ? '1' : '0') + right[1..];

        Assert.False(Totp.TryVerify(secret, wrong, now, lastUsedStep: null, out _));
    }

    [Fact]
    public void A_different_secret_does_not_match()
    {
        var now = DateTimeOffset.UtcNow;
        var code = Totp.Compute(Totp.NewSecret(), Totp.StepAt(now));

        Assert.False(Totp.TryVerify(Totp.NewSecret(), code, now, lastUsedStep: null, out _));
    }

    [Fact]
    public void Secrets_are_the_recommended_length_and_never_repeat()
    {
        var first = Totp.NewSecret();
        var second = Totp.NewSecret();

        Assert.Equal(Totp.SecretBytes, first.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void The_enrollment_uri_carries_what_an_authenticator_needs()
    {
        var secret = Totp.NewSecret();
        var uri = Totp.EnrollmentUri("hyderabadlinks-api", "admin@example.com", secret);

        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains($"secret={Base32.Encode(secret)}", uri);
        Assert.Contains("algorithm=SHA1", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
        // The @ in the address has to be escaped or it breaks the label.
        Assert.Contains("admin%40example.com", uri);
    }
}

public class Base32Tests
{
    /// <summary>The RFC 4648 test vectors, so the encoding is checked against the specification.</summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Matches_the_RFC_4648_test_vectors(string input, string expected) =>
        Assert.Equal(expected, Base32.Encode(Encoding.ASCII.GetBytes(input)));

    [Fact]
    public void A_twenty_byte_secret_encodes_to_thirty_two_characters() =>
        Assert.Equal(32, Base32.Encode(new byte[20]).Length);

    [Fact]
    public void Only_the_base32_alphabet_is_used()
    {
        var encoded = Base32.Encode(Totp.NewSecret());
        Assert.All(encoded, character => Assert.Contains(character, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
    }
}
