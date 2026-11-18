using System.Net;

namespace Roastgate.Tests.Support;

/// <summary>An <see cref="HttpMessageHandler"/> that answers from a script and remembers every requested URL.</summary>
internal sealed class ScriptedHttpHandler(Func<string, HttpResponseMessage> script) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    public static HttpResponseMessage NotFound()
    {
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public static HttpResponseMessage Ok(byte[] body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri!.ToString();
        Requests.Add(url);
        return Task.FromResult(script(url));
    }
}
