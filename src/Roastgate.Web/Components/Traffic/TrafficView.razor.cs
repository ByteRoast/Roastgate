using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Roastgate.Core.AccessLogs;

namespace Roastgate.Web.Components.Traffic;

public partial class TrafficView : IAsyncDisposable
{
    [Inject] private TrafficMap TrafficMap { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    private int TotalRequests => _points.Sum(point => point.Requests) + (_snapshot?.UnlocatedRequests ?? 0);
    private int TotalAddresses => _points.Sum(point => point.Addresses);

    private int CountryCount => _points
        .Select(point => point.Country)
        .Where(country => country is not null)
        .Distinct()
        .Count();

    private TrafficSnapshot? _snapshot;
    private IReadOnlyList<TrafficPoint> _points = [];
    private ElementReference _mapContainer;
    private IJSObjectReference? _module;
    private IJSObjectReference? _worldMap;
    private bool _mapStarted;
    private TimeSpan? _preset = TimeSpan.FromDays(1);
    private DateTime? _customDate;
    private CancellationTokenSource? _loading;
    private bool _isLoading;

    protected override Task OnInitializedAsync()
    {
        return LoadAsync(DateTimeOffset.UtcNow.AddDays(-1));
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

    private async Task OnPresetChangedAsync(TimeSpan? preset)
    {
        _preset = preset;
        _customDate = null;

        if (preset is { } period)
        {
            await LoadAsync(DateTimeOffset.UtcNow - period);
        }
    }

    private async Task OnDateChangedAsync(DateTime? date)
    {
        _customDate = date;
        _preset = null;

        if (date is { } day)
        {
            // Midnight, server time (TZ of the container).
            await LoadAsync(new DateTimeOffset(day.Date));
        }
    }

    private async Task LoadAsync(DateTimeOffset since)
    {
        // A newer period wins: the previous load stops reading the logs.
        if (_loading is not null)
        {
            await _loading.CancelAsync();
        }

        CancellationTokenSource loading = _loading = new CancellationTokenSource();

        _isLoading = true;

        try
        {
            TrafficSnapshot snapshot = await TrafficMap.BuildAsync(since, loading.Token);

            loading.Token.ThrowIfCancellationRequested();

            _snapshot = snapshot;
            _points = [.. _snapshot.Points.OrderByDescending(point => point.Requests)];

            if (_worldMap is not null)
            {
                await _worldMap.InvokeVoidAsync("update", loading.Token, _points);
            }
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
            // Replaced by a newer load: its result no longer matters.
        }
        finally
        {
            if (_loading == loading)
            {
                _isLoading = false;
            }
        }
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
            if (_loading is not null)
            {
                await _loading.CancelAsync();
            }

            if (_worldMap is not null)
            {
                await _worldMap.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The browser is already gone: there is nothing left to free.
        }
    }
}
