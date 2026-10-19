using Microsoft.AspNetCore.Components;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;

namespace Roastgate.Web.Pages.AuditLog;

public partial class Index
{
    [Inject] private IAuditLog Audit { get; set; } = null!;

    private IReadOnlyList<AuditEntry>? _entries;

    protected override async Task OnInitializedAsync()
    {
        _entries = await Audit.ListRecentAsync(200);
    }
}
