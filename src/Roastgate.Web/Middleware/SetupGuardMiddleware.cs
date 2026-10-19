using Roastgate.Core.Setup;

namespace Roastgate.Web.Middleware;

/// <summary>
/// Until the first administrator exists, every page redirects to <c>/setup</c>.
/// Once it exists, <c>/setup</c> no longer exists (404).
/// </summary>
public sealed class SetupGuardMiddleware(RequestDelegate next, ISetupService setup)
{
    private static readonly string[] AllowedBeforeSetup = ["/_blazor", "/_framework", "/_content", "/error", "/error/not-found"];

    public Task InvokeAsync(HttpContext context)
    {
        string path = context.Request.Path.Value ?? "/";
        bool isSetupPath = path.Equals("/setup", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/setup/", StringComparison.OrdinalIgnoreCase);

        if (setup.IsInitialized)
        {
            if (isSetupPath)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            return next(context);
        }

        bool isAsset = Path.HasExtension(path);
        bool isAllowed = isSetupPath
                         || isAsset
                         || AllowedBeforeSetup.Any((string prefix) => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (isAllowed)
        {
            return next(context);
        }

        context.Response.Redirect("/setup");
        return Task.CompletedTask;
    }
}
