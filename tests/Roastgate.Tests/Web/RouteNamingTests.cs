using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Roastgate.Web;

namespace Roastgate.Tests.Web;

/// <summary>Every route must be lowercase, with hyphens between words (e.g. /audit-log, /reverse-proxies/new).</summary>
public sealed partial class RouteNamingTests
{
    // "/" alone, or segments of lowercase words joined by hyphens. A segment may also be a route parameter such as {name} or {id:int}.
    [GeneratedRegex(@"^/(([a-z0-9]+(-[a-z0-9]+)*|\{[A-Za-z0-9_]+(:[a-z]+)?\??\})(/([a-z0-9]+(-[a-z0-9]+)*|\{[A-Za-z0-9_]+(:[a-z]+)?\??\}))*)?$")]
    private static partial Regex ValidRoute();

    public static IEnumerable<object[]> AllPageRoutes()
    {
        Assembly webAssembly = typeof(App).Assembly;
        foreach (Type type in webAssembly.GetTypes())
        {
            foreach (RouteAttribute route in type.GetCustomAttributes<RouteAttribute>())
            {
                yield return [type.Name, route.Template];
            }
        }
    }

    [Fact]
    public void The_application_declares_at_least_one_route()
    {
        Assert.NotEmpty(AllPageRoutes());
    }

    [Theory]
    [MemberData(nameof(AllPageRoutes))]
    public void Route_is_lowercase_and_hyphen_separated(string page, string template)
    {
        Assert.True(ValidRoute().IsMatch(template), $"Route '{template}' of {page} must be lowercase with hyphens between words.");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/audit-log")]
    [InlineData("/reverse-proxies/new")]
    [InlineData("/sites/{name}")]
    [InlineData("/certificates/{id:int}")]
    public void Validator_accepts_conforming_routes(string template)
    {
        Assert.Matches(ValidRoute(), template);
    }

    [Theory]
    [InlineData("/AuditLog")]
    [InlineData("/audit_log")]
    [InlineData("/Audit-Log")]
    [InlineData("/reverse--proxies")]
    [InlineData("/reverse-proxies/")]
    [InlineData("/-audit")]
    public void Validator_rejects_non_conforming_routes(string template)
    {
        Assert.DoesNotMatch(ValidRoute(), template);
    }
}
