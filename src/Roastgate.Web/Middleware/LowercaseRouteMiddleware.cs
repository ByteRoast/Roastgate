namespace Roastgate.Web.Middleware;

/// <summary>
/// Routes are lowercase with hyphens. Page URLs typed with capitals are redirected (301) to their lowercase form.
/// URLs with a file extension (static assets) and framework paths starting with an underscore are left alone.
/// </summary>
public sealed class LowercaseRouteMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        string? path = context.Request.Path.Value;
        bool isReadRequest = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);

        if (isReadRequest
            && path is not null
            && !path.StartsWith("/_", StringComparison.Ordinal)
            && !Path.HasExtension(path)
            && path.Any(char.IsUpper))
        {
            context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            context.Response.Headers.Location = context.Request.PathBase + path.ToLowerInvariant() + context.Request.QueryString;
            return Task.CompletedTask;
        }

        return next(context);
    }
}
