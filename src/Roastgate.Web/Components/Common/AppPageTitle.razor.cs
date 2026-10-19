using Microsoft.AspNetCore.Components;
using Roastgate.Core;

namespace Roastgate.Web.Components.Common;

public partial class AppPageTitle
{
    [Parameter] public string? Title { get; set; }

    private string RealTitle => string.IsNullOrWhiteSpace(Title) ? AppInfo.AppName : Title + " · " + AppInfo.AppName;
}
