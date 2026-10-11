using Microsoft.AspNetCore.Components;
using MudBlazor;
using Roastgate.Core.Nginx;
using Roastgate.Core.Nginx.Whitelists;
using Roastgate.Web.Services;

namespace Roastgate.Web.Components.Whitelists;

public partial class WhitelistListView
{
    [Inject] private IWhitelistService Whitelists { get; set; } = null!;
    [Inject] private IConfigStore Store { get; set; } = null!;
    [Inject] private CurrentUser User { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;

    private IReadOnlyList<WhitelistSummary>? _items;
    private ConfigResult? _result;
    private bool _isBusy;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _items = await Whitelists.ListAsync();
    }

    private static string EditUrl(WhitelistSummary summary)
    {
        // Whitelists with content the list editor cannot represent open in the raw editor straight away.
        string route = summary.EntryCount is null ? "/whitelists/raw" : "/whitelists/edit";
        return route + "?name=" + Uri.EscapeDataString(summary.Name);
    }

    private async Task TestAsync()
    {
        await RunAsync(async () => _result = await Store.TestNginxAsync(await User.GetNameAsync()));
    }

    private async Task ReloadAsync()
    {
        await RunAsync(async () => _result = await Store.ReloadNginxAsync(await User.GetNameAsync()));
    }

    private async Task DeleteAsync(WhitelistSummary summary)
    {
        bool? confirmed = await Dialogs.ShowMessageBoxAsync(
            "Delete " + summary.Name + "?",
            summary.Usage.Count > 0
                ? "It is still included by " + string.Join(", ", summary.Usage.Select((WhitelistUsage item) => item.FileName).Distinct()) + ", so deleting will be refused."
                : "The file is removed and nginx is reloaded. A backup of its content is kept.",
            yesText: "Delete",
            cancelText: "Cancel");
        if (confirmed != true)
        {
            return;
        }

        await RunAsync(async () => _result = await Whitelists.DeleteAsync(summary.Name, reload: true, await User.GetNameAsync()));
    }

    private async Task RunAsync(Func<Task> action)
    {
        _isBusy = true;
        try
        {
            await action();
            await LoadAsync();
        }
        finally
        {
            _isBusy = false;
        }
    }
}
