using Roastgate.Core.Nginx.Modules;

namespace Roastgate.Tests.Core;

public sealed class DirectiveProbeConfigurationTests
{
    [Fact]
    public void Writes_each_directive_in_the_block_of_its_context()
    {
        string configuration = DirectiveProbeConfiguration.Build(
            ["modules/ngx_http_fancyindex_module.so"],
            [
                new ModuleDirective("location_directive", DirectiveContext.Location, "on"),
                new ModuleDirective("http_directive", DirectiveContext.Http, "on"),
                new ModuleDirective("server_directive", DirectiveContext.Server, "off"),
            ]
        );

        Assert.Equal(
            """
                load_module modules/ngx_http_fancyindex_module.so;

                events {
                }

                http {
                    http_directive on;

                    server {
                        server_directive off;

                        location / {
                            location_directive on;
                        }
                    }
                }

                """.ReplaceLineEndings("\n"),
            configuration
        );
    }

    [Fact]
    public void Loads_the_modules_in_the_order_given()
    {
        string configuration = DirectiveProbeConfiguration.Build(
            ["modules/ndk_http_module.so", "/usr/lib/nginx/modules/ngx_http_lua_module.so"],
            []
        );

        Assert.StartsWith(
            "load_module modules/ndk_http_module.so;\nload_module /usr/lib/nginx/modules/ngx_http_lua_module.so;\n",
            configuration
        );
    }

    [Fact]
    public void Keeps_the_events_block_and_empty_blocks_without_modules_or_directives()
    {
        string configuration = DirectiveProbeConfiguration.Build([], []);

        Assert.Equal(
            """
                events {
                }

                http {
                    server {
                        location / {
                        }
                    }
                }

                """.ReplaceLineEndings("\n"),
            configuration
        );
    }
}
