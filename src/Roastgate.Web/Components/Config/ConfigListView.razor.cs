using Microsoft.AspNetCore.Components;
using MudBlazor;
using Roastgate.Core.Nginx;
using Roastgate.Web.Services;

namespace Roastgate.Web.Components.Config;

public partial class ConfigListView
{
    [Inject] private IConfigStore Store { get; set; } = null!;
    [Inject] private CurrentUser User { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Parameter, EditorRequired]
    public ConfigArea Area { get; set; }

    private ConfigListing? _listing;
    private ConfigResult? _result;
    private bool _isBusy;

    private AreaInfo Info => AreaInfo.For(Area);

    /// <summary>Files the wizard created open as a form; everything else opens as raw text.</summary>
    private string OpenUrl(ConfigEntry entry)
    {
        return entry.Wizard ? Info.WizardUrl(entry.Name) : Info.EditUrl(entry.Name);
    }

    protected override async Task OnParametersSetAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _listing = await Store.ListAsync(Area);
    }

    private async Task TestAsync()
    {
        await RunAsync(async () => _result = await Store.TestNginxAsync(await User.GetNameAsync()));
    }

    private async Task ReloadAsync()
    {
        await RunAsync(async () => _result = await Store.ReloadNginxAsync(await User.GetNameAsync()));
    }

    private async Task SetEnabledAsync(ConfigEntry entry, bool enabled)
    {
        await RunAsync(async () => _result = await Store.SetEnabledAsync(Area, entry.Name, enabled, reload: true, await User.GetNameAsync()));
    }

    private async Task DeleteAsync(ConfigEntry entry)
    {
        bool? confirmed = await Dialogs.ShowMessageBoxAsync(
            "Delete " + entry.Name + "?",
            "The file is removed and nginx is reloaded. A backup of its content is kept.",
            yesText: "Delete",
            cancelText: "Cancel");
        if (confirmed != true)
        {
            return;
        }

        await RunAsync(async () => _result = await Store.DeleteAsync(Area, entry.Name, reload: true, await User.GetNameAsync()));
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

    private static string FormatSize(long bytes)
    {
        return bytes < 1024 ? bytes + " B" : (bytes / 1024.0).ToString("0.#") + " KiB";
    }
}
