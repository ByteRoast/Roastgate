using Microsoft.AspNetCore.Components;
using MudBlazor;
using Roastgate.Core.Nginx;

namespace Roastgate.Web.Components.Config;

public partial class ResultAlert
{
    [Parameter]
    public ConfigResult? Result { get; set; }

    [Parameter]
    public string? Class { get; set; }

    private Severity Severity => Result switch
    {
        null => Severity.Info,
        { Succeeded: false } => Severity.Error,
        { Warning: true } => Severity.Warning,
        _ => Severity.Success,
    };
}
