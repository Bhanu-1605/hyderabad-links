using BusinessDirectory.Api.Auth;

namespace BusinessDirectory.UnitTests;

public class AdminPasswordsTests
{
    private const string Password = "a long enough passphrase";

    [Fact]
    public void A_hashed_password_verifies_against_itself()
    {
        var hash = AdminPasswords.Hash(Password);

        Assert.True(AdminPasswords.Verify(Password, hash));
    }

    [Fact]
    public void A_wrong_password_does_not_verify()
    {
        var hash = AdminPasswords.Hash(Password);

        Assert.False(AdminPasswords.Verify(Password + "!", hash));
        Assert.False(AdminPasswords.Verify(Password.ToUpperInvariant(), hash));
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        // A per-password salt means two admins with the same password do not share a hash.
        Assert.NotEqual(AdminPasswords.Hash(Password), AdminPasswords.Hash(Password));
    }

    [Fact]
    public void The_stored_value_never_contains_the_password()
    {
        Assert.DoesNotContain("passphrase", AdminPasswords.Hash(Password), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_stored_value_records_the_algorithm_and_iterations()
    {
        var parts = AdminPasswords.Hash(Password).Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.True(int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) >= 100_000);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$210000$not-base64!$also-not-base64!")]
    [InlineData("pbkdf2-sha256$1$c2FsdA==$aGFzaA==")]          // iteration count too low to trust
    [InlineData("argon2$210000$c2FsdA==$aGFzaA==")]            // an algorithm this code does not implement
    public void An_unusable_stored_value_fails_instead_of_throwing(string? storedHash)
    {
        Assert.False(AdminPasswords.Verify(Password, storedHash));
    }

    [Fact]
    public void An_empty_password_never_verifies()
    {
        // An invited admin has no password yet: password_hash is null and nothing may sign in as them.
        Assert.False(AdminPasswords.Verify("", AdminPasswords.Hash(Password)));
        Assert.False(AdminPasswords.Verify(null, AdminPasswords.Hash(Password)));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("short", false)]
    [InlineData("12345678901", false)]          // 11 characters
    [InlineData("123456789012", true)]          // 12 characters
    [InlineData("            ", false)]         // spaces are not a password
    public void Password_strength_is_judged_by_length(string? password, bool expected)
    {
        Assert.Equal(expected, AdminPasswords.IsStrongEnough(password));
    }
}
