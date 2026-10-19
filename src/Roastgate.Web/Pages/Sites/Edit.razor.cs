using Microsoft.AspNetCore.Components;

namespace Roastgate.Web.Pages.Sites;

public partial class Edit
{
    [SupplyParameterFromQuery(Name = "name")]
    private string? Name { get; set; }
}
