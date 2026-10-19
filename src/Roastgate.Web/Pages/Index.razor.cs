using Microsoft.AspNetCore.Components;
using Roastgate.Core;
using Roastgate.Core.Hosting;
using Roastgate.Core.Nginx;
using Roastgate.Web.Services;

namespace Roastgate.Web.Pages;

public partial class Index
{
    [Inject] private NginxProbe Probe { get; set; } = null!;
    [Inject] private RoastgateOptions Options { get; set; } = null!;
    [Inject] private IConfigStore Store { get; set; } = null!;
    [Inject] private CurrentUser User { get; set; } = null!;

    private NginxProbeResult? _probe;
    private bool _isChecking;
    private ConfigResult? _result;

    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync();
    }

    private async Task TestAsync()
    {
        _result = await Store.TestNginxAsync(await User.GetNameAsync());
    }

    private async Task ReloadAsync()
    {
        _result = await Store.ReloadNginxAsync(await User.GetNameAsync());
    }

    private async Task RefreshAsync()
    {
        _isChecking = true;
        _probe = null;
        try
        {
            _probe = await Probe.ProbeAsync();
        }
        finally
        {
            _isChecking = false;
        }
    }
}
