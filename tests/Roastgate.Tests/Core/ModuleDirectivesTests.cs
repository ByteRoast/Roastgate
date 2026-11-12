using Roastgate.Core.Nginx.Modules;

namespace Roastgate.Tests.Core;

public sealed class ModuleDirectivesTests
{
    private static ModuleDirectives FancyIndexAccepts(params string[] directives)
    {
        return new ModuleDirectives(
            true,
            new Dictionary<string, IReadOnlySet<string>>
            {
                [KnownModule.FancyIndex.Id] = new HashSet<string>(directives, StringComparer.Ordinal),
            }
        );
    }

    [Fact]
    public void Supports_a_directive_nginx_accepted()
    {
        ModuleDirectives directives = FancyIndexAccepts("fancyindex_case_sensitive");

        Assert.True(directives.Supports(KnownModule.FancyIndex, "fancyindex_case_sensitive"));
    }

    [Fact]
    public void Does_not_support_a_directive_nginx_refused()
    {
        ModuleDirectives directives = FancyIndexAccepts();

        Assert.False(directives.Supports(KnownModule.FancyIndex, "fancyindex_case_sensitive"));
    }

    [Fact]
    public void Does_not_support_a_directive_of_a_module_that_was_not_probed()
    {
        ModuleDirectives directives = new(true, new Dictionary<string, IReadOnlySet<string>>());

        Assert.False(directives.Supports(KnownModule.FancyIndex, "fancyindex_case_sensitive"));
    }

    [Fact]
    public void Supports_nothing_when_nginx_could_not_be_asked()
    {
        Assert.False(ModuleDirectives.Unknown.Known);
        Assert.False(ModuleDirectives.Unknown.Supports(KnownModule.FancyIndex, "fancyindex_case_sensitive"));
    }
}
