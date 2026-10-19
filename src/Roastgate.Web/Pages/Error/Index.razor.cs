using System.Diagnostics;
using Microsoft.AspNetCore.Components;

namespace Roastgate.Web.Pages.Error;

public partial class Index
{
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    private string? RequestId { get; set; }

    protected override void OnInitialized()
    {
        RequestId = Activity.Current?.Id ?? HttpContext?.TraceIdentifier;
    }
}
