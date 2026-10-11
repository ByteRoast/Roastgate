using Microsoft.AspNetCore.Components;

namespace Roastgate.Web.Components.Pages;

public partial class ReverseProxyEdit
{
    [SupplyParameterFromQuery(Name = "name")]
    private string? Name { get; set; }
}
