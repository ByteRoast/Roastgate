using Microsoft.AspNetCore.Components;

namespace Roastgate.Web.Components.Pages;

public partial class SiteWizard
{
    [SupplyParameterFromQuery(Name = "name")]
    private string? Name { get; set; }
}
