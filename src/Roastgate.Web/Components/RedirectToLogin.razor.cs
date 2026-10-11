using Microsoft.AspNetCore.Components;

namespace Roastgate.Web.Components;

public partial class RedirectToLogin
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    protected override void OnInitialized()
    {
        string returnUrl = "/" + Navigation.ToBaseRelativePath(Navigation.Uri);
        Navigation.NavigateTo($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}", forceLoad: true);
    }
}
