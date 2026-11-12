using Roastgate.Core.Nginx.Modules;

namespace Roastgate.Tests.Core;

public sealed class UnknownDirectiveErrorTests
{
    [Fact]
    public void Finds_the_directive_nginx_does_not_know()
    {
        string output = """
                        nginx: [emerg] unknown directive "fancyindex_case_sensitive" in /etc/nginx/.roastgate-probe/3f2a.conf:12
                        nginx: configuration file /etc/nginx/.roastgate-probe/3f2a.conf test failed
                        """;

        Assert.Equal("fancyindex_case_sensitive", UnknownDirectiveError.Find(output));
    }

    [Theory]
    [InlineData("""nginx: [emerg] invalid value "maybe" in /etc/nginx/.roastgate-probe/3f2a.conf:12""")]
    [InlineData(
        """nginx: [emerg] dlopen() "/usr/lib/nginx/modules/x.so" failed (x.so: cannot open shared object file)""")]
    [InlineData("""nginx: [emerg] "fancyindex_case_sensitive" directive is not allowed here in /etc/nginx/x.conf:3""")]
    [InlineData("nginx: the configuration file /etc/nginx/.roastgate-probe/3f2a.conf syntax is ok")]
    [InlineData("")]
    public void Finds_nothing_when_nginx_failed_for_another_reason(string output)
    {
        Assert.Null(UnknownDirectiveError.Find(output));
    }
}
