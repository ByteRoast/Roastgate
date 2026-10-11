using System.Text;
using Roastgate.Core;
using Roastgate.Core.Security;

namespace Roastgate.Tests.Security;

public sealed class TotpTests
{
    // RFC 6238, appendix B: the SHA-1 test secret is the ASCII string "12345678901234567890". The RFC lists 8 digit codes; ours are the last 6.
    private static readonly byte[] RfcKey = Encoding.ASCII.GetBytes("12345678901234567890");
    private static readonly string RfcSecret = Base32.Encode(RfcKey);

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void The_codes_match_the_published_test_vectors(long unixSeconds, string expected)
    {
        Assert.Equal(expected, Totp.Compute(RfcKey, unixSeconds / Totp.StepSeconds));
    }

    [Fact]
    public void The_step_of_a_moment_is_its_unix_time_divided_by_thirty_seconds()
    {
        Assert.Equal(0, Totp.StepOf(DateTimeOffset.FromUnixTimeSeconds(29)));
        Assert.Equal(1, Totp.StepOf(DateTimeOffset.FromUnixTimeSeconds(30)));
        Assert.Equal(1111111109L / 30, Totp.StepOf(DateTimeOffset.FromUnixTimeSeconds(1111111109)));
    }

    // ---- Base32 ----------------------------------------------------------------------

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_matches_the_rfc_4648_vectors(string text, string encoded)
    {
        Assert.Equal(encoded, Base32.Encode(Encoding.ASCII.GetBytes(text)));
        Assert.Equal(text, Encoding.ASCII.GetString(Base32.Decode(encoded)!));
    }

    [Fact]
    public void Base32_tolerates_the_way_secrets_are_typed()
    {
        byte[] expected = Encoding.ASCII.GetBytes("foobar");

        Assert.Equal(expected, Base32.Decode("mzxw 6ytb-oi"));
        Assert.Equal(expected, Base32.Decode("MZXW6YTBOI======"));
    }

    [Theory]
    [InlineData("MZXW6!")]
    [InlineData("0189")]
    [InlineData("MZXW6é")]
    public void Base32_rejects_characters_outside_its_alphabet(string text)
    {
        Assert.Null(Base32.Decode(text));
    }

    [Fact]
    public void Random_bytes_survive_a_round_trip()
    {
        for (int length = 0; length < 40; length++)
        {
            byte[] data = System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);

            Assert.Equal(data, Base32.Decode(Base32.Encode(data)));
        }
    }

    // ---- Verification ----------------------------------------------------------------

    [Fact]
    public void The_code_of_the_current_step_is_accepted_and_the_step_is_reported()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1111111109);

        Assert.True(Totp.TryVerify(RfcSecret, "081804", now, null, out long step));

        Assert.Equal(1111111109L / 30, step);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void One_step_of_clock_drift_either_way_is_tolerated(int drift)
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        string code = Totp.Compute(RfcKey, Totp.StepOf(now) + drift);

        Assert.True(Totp.TryVerify(RfcSecret, code, now, null, out _));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    [InlineData(10)]
    public void Two_steps_or_more_are_refused(int drift)
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        string code = Totp.Compute(RfcKey, Totp.StepOf(now) + drift);

        Assert.False(Totp.TryVerify(RfcSecret, code, now, null, out _));
    }

    [Fact]
    public void A_code_cannot_be_used_twice_nor_can_an_older_one_after_a_newer_one()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        long step = Totp.StepOf(now);
        string current = Totp.Compute(RfcKey, step);
        string previous = Totp.Compute(RfcKey, step - 1);

        Assert.True(Totp.TryVerify(RfcSecret, current, now, null, out long used));
        Assert.False(Totp.TryVerify(RfcSecret, current, now, used, out _));
        Assert.False(Totp.TryVerify(RfcSecret, previous, now, used, out _));
        Assert.True(Totp.TryVerify(RfcSecret, Totp.Compute(RfcKey, step + 1), now, used, out long next));
        Assert.Equal(step + 1, next);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("12 34a6")]
    [InlineData("081804 ; DROP")]
    public void Anything_that_is_not_six_digits_is_refused(string code)
    {
        Assert.False(Totp.TryVerify(RfcSecret, code, DateTimeOffset.FromUnixTimeSeconds(1111111109), null, out _));
    }

    [Fact]
    public void Spaces_inside_the_code_are_ignored_because_apps_display_it_in_two_groups()
    {
        Assert.True(Totp.TryVerify(RfcSecret, "081 804", DateTimeOffset.FromUnixTimeSeconds(1111111109), null, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base32!")]
    public void A_broken_secret_never_verifies_anything(string secret)
    {
        Assert.False(Totp.TryVerify(secret, "081804", DateTimeOffset.FromUnixTimeSeconds(1111111109), null, out _));
    }

    [Fact]
    public void Generated_secrets_are_160_bits_and_never_repeat()
    {
        string first = Totp.GenerateSecret();
        string second = Totp.GenerateSecret();

        Assert.Equal(32, first.Length);
        Assert.Matches("^[A-Z2-7]{32}$", first);
        Assert.NotEqual(first, second);
        Assert.Equal(20, Base32.Decode(first)!.Length);
    }

    [Fact]
    public void The_provisioning_uri_has_everything_an_authenticator_app_reads()
    {
        string uri = Totp.ProvisioningUri(AppInfo.AppName, "admin", "JBSWY3DPEHPK3PXP");

        Assert.Equal("otpauth://totp/Roastgate:admin?secret=JBSWY3DPEHPK3PXP&issuer=Roastgate&algorithm=SHA1&digits=6&period=30", uri);
    }

    [Fact]
    public void The_provisioning_uri_escapes_what_could_break_it()
    {
        string uri = Totp.ProvisioningUri("My Gate & Co", "a b@c", "ABC");

        Assert.StartsWith("otpauth://totp/My%20Gate%20%26%20Co:a%20b%40c?", uri);
        Assert.DoesNotContain(" ", uri);
    }

    // ---- Recovery codes --------------------------------------------------------------

    [Fact]
    public void Ten_distinct_codes_of_four_groups_of_four_are_generated()
    {
        IReadOnlyList<string> codes = RecoveryCodes.Generate();

        Assert.Equal(RecoveryCodes.Count, codes.Count);
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, (string code) => Assert.Matches("^[a-z2-7]{4}(-[a-z2-7]{4}){3}$", code));
    }

    [Theory]
    [InlineData("abcd-efgh-ijkl-mnop", "abcdefghijklmnop")]
    [InlineData("ABCD EFGH IJKL MNOP", "abcdefghijklmnop")]
    [InlineData("  abcdefghijklmnop ", "abcdefghijklmnop")]
    public void A_typed_code_is_normalised_whatever_its_case_and_separators(string typed, string normalized)
    {
        Assert.Equal(normalized, RecoveryCodes.Normalize(typed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456")]
    [InlineData("abcd-efgh-ijkl")]
    [InlineData("abcd-efgh-ijkl-mnop-qrst")]
    [InlineData("abcd-efgh-ijkl-mno1")]
    [InlineData("abcd-efgh-ijkl-mno!")]
    public void Anything_else_is_not_a_recovery_code(string typed)
    {
        Assert.Null(RecoveryCodes.Normalize(typed));
    }

    [Fact]
    public void The_stored_hash_is_stable_and_does_not_reveal_the_code()
    {
        string hash = RecoveryCodes.Hash("abcdefghijklmnop");

        Assert.Equal(hash, RecoveryCodes.Hash("abcdefghijklmnop"));
        Assert.NotEqual(hash, RecoveryCodes.Hash("abcdefghijklmnoq"));
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.DoesNotContain("abcdefghijklmnop", hash);
    }
}
