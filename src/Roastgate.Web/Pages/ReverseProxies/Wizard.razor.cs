using Microsoft.AspNetCore.Components;

namespace Roastgate.Web.Pages.ReverseProxies;

public partial class Wizard
{
    [SupplyParameterFromQuery(Name = "name")]
    private string? Name { get; set; }
}
