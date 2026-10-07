using Microsoft.AspNetCore.Components.Authorization;

namespace Roastgate.Web.Services;

/// <summary>The signed-in administrator, as written in the audit log.</summary>
public sealed class CurrentUser(AuthenticationStateProvider authenticationState)
{
    public async Task<string> GetNameAsync()
    {
        AuthenticationState state = await authenticationState.GetAuthenticationStateAsync();
        return state.User.Identity?.Name ?? "unknown";
    }
}
