using Roastgate.Core.Hosting;
using Roastgate.Core.Nginx.Modules;

namespace Roastgate.Tests.Core;

public class KnownModuleTests
{
    [Fact]
    public void Recognises_fancyindex_when_its_shared_object_is_loaded()
    {
        NginxModules modules = NginxModules.Parse("load_module modules/ngx_http_fancyindex_module.so;");
        
        Assert.True(KnownModule.FancyIndex.IsLoaded(modules));
    }

    [Fact]
    public void Does_not_report_a_module_as_loaded_when_the_list_is_unknown()
    {
        NginxModules modules = NginxModules.Unknown;
        
        Assert.False(KnownModule.FancyIndex.IsLoaded(modules));
    }

    [Fact]
    public void Does_not_recognise_fancyindex_when_only_other_modules_are_loaded()
    {
        NginxModules modules = NginxModules.Parse("load_module modules/ngx_mail_module.so;");

        Assert.False(KnownModule.FancyIndex.IsLoaded(modules));
    }

    [Fact]
    public void Fancyindex_declares_case_sensitive_as_an_optional_location_directive()
    {
        ModuleDirective directive = Assert.Single(
            KnownModule.FancyIndex.OptionalDirectives,
            directive => directive.Name == "fancyindex_case_sensitive"
        );

        Assert.Equal(DirectiveContext.Location, directive.Context);
    }

    [Fact]
    public void Optional_directives_can_be_written_into_a_configuration_file()
    {
        IEnumerable<ModuleDirective> directives = KnownModule.All.SelectMany(module => module.OptionalDirectives);

        Assert.All(
            directives,
            directive =>
            {
                Assert.Matches("^[a-z0-9_]+$", directive.Name);
                Assert.False(string.IsNullOrEmpty(directive.SampleValue));
                Assert.DoesNotMatch(@"[\s;{}#]", directive.SampleValue);
            }
        );
    }
}
