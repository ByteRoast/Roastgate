using Microsoft.AspNetCore.Components;

namespace Roastgate.Web.Pages.Whitelists;

public partial class RawEdit
{
    [SupplyParameterFromQuery(Name = "name")]
    private string? Name { get; set; }
}
