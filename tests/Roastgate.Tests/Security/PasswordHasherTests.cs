using Roastgate.Core.Security;

namespace Roastgate.Tests.Security;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new(1000);

    [Fact]
    public void A_hash_verifies_with_the_right_password_only()
    {
        string hash = _hasher.Hash("correct horse battery staple");

        Assert.Equal(PasswordVerification.Success, _hasher.Verify("correct horse battery staple", hash));
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify("correct horse battery stapl3", hash));
    }

    [Fact]
    public void The_same_password_never_produces_the_same_hash()
    {
        Assert.NotEqual(_hasher.Hash("same password 123"), _hasher.Hash("same password 123"));
    }

    [Fact]
    public void The_hash_documents_its_scheme_and_iteration_count()
    {
        string[] parts = _hasher.Hash("anything at all 1!").Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha512", parts[0]);
        Assert.Equal("1000", parts[1]);
    }

    [Fact]
    public void A_hash_made_with_fewer_iterations_is_flagged_for_an_upgrade()
    {
        string weak = new PasswordHasher(10).Hash("upgrade me please 1!");

        Assert.Equal(PasswordVerification.SuccessRehashNeeded, _hasher.Verify("upgrade me please 1!", weak));
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify("another password 22", weak));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("pbkdf2-sha512$abc$AAAA$AAAA")]
    [InlineData("pbkdf2-sha512$1000$not-base64!$AAAA")]
    [InlineData("pbkdf2-sha512$1000$AAAA$")]
    [InlineData("other-scheme$1000$AAAA$AAAA")]
    [InlineData("pbkdf2-sha512$0$AAAA$AAAA")]
    public void Malformed_stored_hashes_never_verify_and_never_throw(string stored)
    {
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify("whatever password 1!", stored));
    }
}
