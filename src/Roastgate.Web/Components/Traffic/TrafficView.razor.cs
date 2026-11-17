using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Roastgate.Core.AccessLogs;

namespace Roastgate.Web.Components.Traffic;

public partial class TrafficView
{
    [Inject] private TrafficMap TrafficMap { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    private TrafficSnapshot? _snapshot;
    private IReadOnlyList<TrafficPoint> _points = [];
    private ElementReference _map;
    private bool _mapDrawn;

    protected override async Task OnInitializedAsync()
    {
        _snapshot = await TrafficMap.BuildAsync(DateTimeOffset.UtcNow.AddDays(-1));
        _points = [.. _snapshot.Points.OrderByDescending(point => point.Requests)];
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_snapshot is null || _mapDrawn)
        {
            return;
        }

        _mapDrawn = true;

        await using IJSObjectReference module = await JS.InvokeAsync<IJSObjectReference>(
            "import",
            "./Components/Traffic/TrafficView.razor.js"
        );

        await module.InvokeVoidAsync("render", _map, "maps/world.svg", _points);
    }
}
