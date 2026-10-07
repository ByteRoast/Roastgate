using Roastgate.Core.Security;

namespace Roastgate.Tests.Security;

public sealed class CredentialPolicyTests
{
    [Theory]
    [InlineData("admin")]
    [InlineData("oskar.d")]
    [InlineData("a1_b-c")]
    public void Valid_usernames_are_accepted(string username)
    {
        Assert.Empty(CredentialPolicy.ValidateUsername(username));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("Admin")]
    [InlineData("-admin")]
    [InlineData("has space")]
    [InlineData("admin\n")]
    [InlineData("a-very-long-username-that-goes-way-past-the-limit")]
    public void Invalid_usernames_are_rejected(string username)
    {
        Assert.NotEmpty(CredentialPolicy.ValidateUsername(username));
    }

    [Theory]
    [InlineData("Tr0ub4dor&3xyz")]
    [InlineData("correct horse battery staple")]
    [InlineData("MixedCase-and-symbols")]
    public void Strong_passwords_are_accepted(string password)
    {
        Assert.Empty(CredentialPolicy.ValidatePassword(password, "admin"));
    }

    [Fact]
    public void Short_passwords_are_rejected()
    {
        Assert.NotEmpty(CredentialPolicy.ValidatePassword("Sh0rt!pw", "admin"));
    }

    [Fact]
    public void A_missing_password_is_rejected()
    {
        Assert.NotEmpty(CredentialPolicy.ValidatePassword(null, "admin"));
    }

    [Fact]
    public void Overly_long_passwords_are_rejected()
    {
        Assert.NotEmpty(CredentialPolicy.ValidatePassword(new string('a', CredentialPolicy.MaximumPasswordLength + 1), "admin"));
    }

    [Fact]
    public void Passwords_containing_the_username_are_rejected()
    {
        Assert.NotEmpty(CredentialPolicy.ValidatePassword("My-ADMIN-Passw0rd!", "admin"));
    }

    [Fact]
    public void Repetitive_passwords_are_rejected()
    {
        Assert.NotEmpty(CredentialPolicy.ValidatePassword("aaaaaaaaaaaa1A", "admin"));
    }

    [Theory]
    [InlineData("lowerUPPERabc")]
    [InlineData("abcdefg1234567")]
    [InlineData("ABCDEFG1234567")]
    public void A_password_under_20_characters_needs_at_least_three_character_classes(string password)
    {
        IReadOnlyList<string> problems = CredentialPolicy.ValidatePassword(password, "admin");

        Assert.Contains(problems, (string problem) => problem.Contains("at least 3 of"));
    }

    [Fact]
    public void A_passphrase_of_20_characters_needs_no_character_classes()
    {
        Assert.Empty(CredentialPolicy.ValidatePassword("abcdefghijklmnopqrst", "admin"));
    }
}
