using Roastgate.Core.Hosting;

namespace Roastgate.Tests.Core;

public sealed class NginxModulesTests
{
    [Fact]
    public void Reads_the_shared_object_name_of_a_loaded_module()
    {
        NginxModules nginxModules = NginxModules.Parse("load_module modules/ngx_http_fancyindex_module.so;");

        Assert.True(nginxModules.Known);
        Assert.True(nginxModules.Has("ngx_http_fancyindex_module.so"));
    }

    [Theory]
    [InlineData("load_module \"modules/x.so\";", "x.so")]
    [InlineData("    load_module /usr/lib/nginx/modules/x.so;", "x.so")]
    [InlineData("load_module modules/x.so; # note", "x.so")]
    public void Reads_the_module_name_whether_quoted_absolute_or_followed_by_a_comment(string line, string expectedName)
    {
        NginxModules nginxModules = NginxModules.Parse(line);

        Assert.True(nginxModules.Has(expectedName));
    }

    [Fact]
    public void Ignores_a_commented_load_module_line()
    {
        NginxModules nginxModules = NginxModules.Parse("# load_module modules/x.so;");

        Assert.False(nginxModules.Has("x.so"));
    }

    [Fact]
    public void Ignores_a_directive_that_only_mentions_the_module_name()
    {
        NginxModules nginxModules = NginxModules.Parse("fancyindex on;");

        Assert.False(nginxModules.Has("fancyindex"));
        Assert.True(nginxModules.Known);
    }

    [Fact]
    public void Reads_every_loaded_module_from_the_output()
    {
        string fakeOutput = """
                            load_module modules/ngx_mail_module.so;

                            load_module modules/ngx_http_fancyindex_module.so;
                            """;

        NginxModules nginxModules = NginxModules.Parse(fakeOutput);

        Assert.True(nginxModules.Has("ngx_mail_module.so"));
        Assert.True(nginxModules.Has("ngx_http_fancyindex_module.so"));
    }

    [Fact]
    public void Reports_no_module_when_the_list_is_unknown()
    {
        Assert.False(NginxModules.Unknown.Known);
        Assert.False(NginxModules.Unknown.Has("x.so"));
        Assert.Empty(NginxModules.Unknown.LoadPaths);
    }

    [Fact]
    public void Keeps_the_load_paths_in_the_order_nginx_loads_them()
    {
        string fakeOutput = """
                            load_module modules/ndk_http_module.so;
                            load_module /usr/lib/nginx/modules/ngx_http_lua_module.so;
                            """;

        NginxModules nginxModules = NginxModules.Parse(fakeOutput);

        Assert.Equal(
            ["modules/ndk_http_module.so", "/usr/lib/nginx/modules/ngx_http_lua_module.so"],
            nginxModules.LoadPaths
        );
    }

    [Theory]
    [InlineData("load_module modules/x.so;", "modules/x.so")]
    [InlineData("load_module \"modules/x.so\";", "modules/x.so")]
    [InlineData("load_module /usr/lib/nginx/modules/x.so;", "/usr/lib/nginx/modules/x.so")]
    public void Keeps_the_load_path_as_written_without_resolving_it(string line, string expectedPath)
    {
        NginxModules nginxModules = NginxModules.Parse(line);

        Assert.Equal(expectedPath, Assert.Single(nginxModules.LoadPaths));
    }

    [Fact]
    public void Lists_a_load_path_once_when_it_appears_twice()
    {
        string fakeOutput = """
                            load_module modules/x.so;
                            load_module modules/x.so;
                            """;

        NginxModules nginxModules = NginxModules.Parse(fakeOutput);

        Assert.Single(nginxModules.LoadPaths);
    }
}
