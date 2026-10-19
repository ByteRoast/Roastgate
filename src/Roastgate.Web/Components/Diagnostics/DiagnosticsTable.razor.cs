using Microsoft.AspNetCore.Components;
using Roastgate.Core.Setup;

namespace Roastgate.Web.Components.Diagnostics;

public partial class DiagnosticsTable
{
    [Parameter, EditorRequired]
    public IReadOnlyList<DiagnosticCheck> Checks { get; set; } = [];
}
