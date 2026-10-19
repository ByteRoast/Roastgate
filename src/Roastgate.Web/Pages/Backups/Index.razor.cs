using Microsoft.AspNetCore.Components;

namespace Roastgate.Web.Pages.Backups;

public partial class Index
{
    [SupplyParameterFromQuery(Name = "area")]
    private string? Area { get; set; }

    [SupplyParameterFromQuery(Name = "name")]
    private string? Name { get; set; }
}
