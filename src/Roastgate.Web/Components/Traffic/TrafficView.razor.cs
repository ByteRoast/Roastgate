using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Roastgate.Core.AccessLogs;

namespace Roastgate.Web.Components.Traffic;

public partial class TrafficView : IAsyncDisposable
{
    [Inject] private TrafficMap TrafficMap { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    private TrafficSnapshot? _snapshot;
    private IReadOnlyList<TrafficPoint> _points = [];
    private ElementReference _mapContainer;
    private IJSObjectReference? _module;
    private IJSObjectReference? _worldMap;
    private bool _mapStarted;

    protected override async Task OnInitializedAsync()
    {
        _snapshot = await TrafficMap.BuildAsync(DateTimeOffset.UtcNow.AddDays(-1));
        _points = [.. _snapshot.Points.OrderByDescending(point => point.Requests)];
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_snapshot is null || _mapStarted)
        {
            return;
        }

        _mapStarted = true;

        _module = await JS.InvokeAsync<IJSObjectReference>("import", "./Components/Traffic/TrafficView.razor.js");
        _worldMap = await _module.InvokeAsync<IJSObjectReference>("init", _mapContainer, "maps/world.svg");

        await _worldMap.InvokeVoidAsync("update", _points);
    }

    private async Task ZoomByAsync(double factor)
    {
        if (_worldMap is not null)
        {
            await _worldMap.InvokeVoidAsync("zoomBy", factor);
        }
    }

    private async Task ResetZoomAsync()
    {
        if (_worldMap is not null)
        {
            await _worldMap.InvokeVoidAsync("reset");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_worldMap is not null) await _worldMap.DisposeAsync();
            if (_module is not null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The browser is already gone: there is nothing left to free.
        }
    }
}
