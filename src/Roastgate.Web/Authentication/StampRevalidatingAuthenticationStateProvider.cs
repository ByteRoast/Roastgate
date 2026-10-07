using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Roastgate.Core.Security;

namespace Roastgate.Web.Authentication;

/// <summary>
/// Blazor circuits are long lived: the cookie is only read when the circuit starts. This checks the security stamp again every
/// half minute, so a tab left open stops working soon after the password was changed from somewhere else.
/// </summary>
public sealed class StampRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        if (authenticationState.User.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ISessionStamp sessionStamp = scope.ServiceProvider.GetRequiredService<ISessionStamp>();
        return await AuthenticationHelper.IsCurrentAsync(authenticationState.User, sessionStamp, cancellationToken);
    }
}
