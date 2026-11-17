using Microsoft.AspNetCore.Components;

namespace Roastgate.Web.Components.Traffic;

public partial class CountryFlag
{
    [Parameter] public string? Code { get; set; }
}
