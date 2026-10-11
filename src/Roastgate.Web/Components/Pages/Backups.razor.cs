using Microsoft.AspNetCore.Components;

namespace Roastgate.Web.Components.Pages;

public partial class Backups
{
    [SupplyParameterFromQuery(Name = "area")]
    private string? Area { get; set; }

    [SupplyParameterFromQuery(Name = "name")]
    private string? Name { get; set; }
}
