using Microsoft.AspNetCore.Components;

namespace Roastgate.Web.Components.Pages;

public partial class SiteEdit
{
    [SupplyParameterFromQuery(Name = "name")]
    private string? Name { get; set; }
}
