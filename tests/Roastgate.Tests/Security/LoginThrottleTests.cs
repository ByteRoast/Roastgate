using Microsoft.Extensions.Time.Testing;
using Roastgate.Core.Security;

namespace Roastgate.Tests.Security;

public sealed class LoginThrottleTests
{
    private readonly FakeTimeProvider _time = new();

    private LoginThrottle CreateThrottle()
    {
        return new LoginThrottle(_time);
    }

    private static void Fail(LoginThrottle throttle, string key, int times)
    {
        for (int index = 0; index < times; index++)
        {
            throttle.RecordFailure(key);
        }
    }

    [Fact]
    public void A_key_is_not_blocked_below_the_limit()
    {
        LoginThrottle throttle = CreateThrottle();

        Fail(throttle, "1.2.3.4", LoginThrottle.MaxFailures - 1);

        Assert.False(throttle.IsBlocked("1.2.3.4", out _));
    }

    [Fact]
    public void A_key_is_blocked_at_the_limit_and_told_how_long_to_wait()
    {
        LoginThrottle throttle = CreateThrottle();

        Fail(throttle, "1.2.3.4", LoginThrottle.MaxFailures);

        Assert.True(throttle.IsBlocked("1.2.3.4", out TimeSpan retryAfter));
        Assert.Equal(LoginThrottle.Window, retryAfter);
    }

    [Fact]
    public void Other_keys_are_not_affected()
    {
        LoginThrottle throttle = CreateThrottle();

        Fail(throttle, "1.2.3.4", LoginThrottle.MaxFailures);

        Assert.False(throttle.IsBlocked("5.6.7.8", out _));
    }

    [Fact]
    public void The_block_lifts_once_the_window_has_passed()
    {
        LoginThrottle throttle = CreateThrottle();
        Fail(throttle, "1.2.3.4", LoginThrottle.MaxFailures);

        _time.Advance(LoginThrottle.Window + TimeSpan.FromSeconds(1));

        Assert.False(throttle.IsBlocked("1.2.3.4", out _));
    }

    [Fact]
    public void Failures_slide_out_of_the_window_one_by_one()
    {
        LoginThrottle throttle = CreateThrottle();
        throttle.RecordFailure("1.2.3.4");
        _time.Advance(TimeSpan.FromMinutes(10));
        Fail(throttle, "1.2.3.4", LoginThrottle.MaxFailures - 1);
        Assert.True(throttle.IsBlocked("1.2.3.4", out TimeSpan retryAfter));
        Assert.Equal(TimeSpan.FromMinutes(5), retryAfter);

        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(throttle.IsBlocked("1.2.3.4", out _));
    }

    [Fact]
    public void A_successful_sign_in_clears_the_failures()
    {
        LoginThrottle throttle = CreateThrottle();
        Fail(throttle, "1.2.3.4", LoginThrottle.MaxFailures);

        throttle.Reset("1.2.3.4");

        Assert.False(throttle.IsBlocked("1.2.3.4", out _));
    }
}
