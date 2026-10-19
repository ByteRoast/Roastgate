using Microsoft.AspNetCore.Components.Routing;
using static Microsoft.AspNetCore.Components.Web.RenderMode;
using Microsoft.AspNetCore.Components;

namespace Roastgate.Web;

public partial class App
{
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    // Pages marked [ExcludeFromInteractiveRouting] (login) render statically: they must be able to set the auth cookie.
    private IComponentRenderMode? PageRenderMode =>
        HttpContext is not null && HttpContext.AcceptsInteractiveRouting() ? InteractiveServer : null;
}
