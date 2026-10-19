using Microsoft.AspNetCore.Components;
using MudBlazor;
using Roastgate.Core.Data.Interfaces;
using Roastgate.Core.Data.Models;
using Roastgate.Core.Nginx;
using Roastgate.Web.Services;

namespace Roastgate.Web.Components.Backups;

public partial class BackupsView
{
    [Inject] private IConfigBackupStore Backups { get; set; } = null!;
    [Inject] private IConfigStore Store { get; set; } = null!;
    [Inject] private CurrentUser User { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;

    private sealed record DiffRow(DiffLine? Line, int Skipped);

    private const int ContextLines = 3;

    [Parameter] public string? Area { get; set; }

    [Parameter] public string? Name { get; set; }

    private IReadOnlyList<ConfigBackup>? _items;
    private ConfigBackup? _selected;
    private string? _currentContent;
    private DiffResult? _diff;
    private ConfigResult? _result;
    private string _area = string.Empty;
    private string _name = string.Empty;
    private bool _isBusy;
    private bool _initialised;

    protected override async Task OnParametersSetAsync()
    {
        if (_initialised)
        {
            return;
        }

        _initialised = true;
        _area = Enum.TryParse(Area, out ConfigArea parsed) && Enum.IsDefined(parsed) ? parsed.ToString() : string.Empty;
        _name = Name ?? string.Empty;

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        string? area = string.IsNullOrEmpty(_area) ? null : _area;
        string? name = string.IsNullOrWhiteSpace(_name) ? null : _name.Trim();

        _items = await Backups.ListAsync(area, name, 200);
        _selected = null;
    }

    private async Task SelectAsync(ConfigBackup backup)
    {
        _selected = backup;
        _result = null;

        await ShowDiffAsync(backup);
    }

    private async Task ShowDiffAsync(ConfigBackup backup)
    {
        _currentContent = null;
        _diff = null;

        if (Enum.TryParse(backup.Area, out ConfigArea area) && Enum.IsDefined(area))
        {
            ConfigFile? current = await Store.ReadAsync(area, backup.Name);

            _currentContent = current?.Content;

            if (current is not null)
            {
                // What restoring would do: from the current content to the backed up one.
                _diff = LineDiff.Compute(current.Content, backup.Content);
            }
        }
    }

    private async Task RestoreAsync()
    {
        if (_selected is null)
        {
            return;
        }

        bool? confirmed = await Dialogs.ShowMessageBoxAsync(
            "Restore " + _selected.Name + "?",
            "The file is replaced by this version and nginx is reloaded. The content it replaces is backed up too.",
            yesText: "Restore",
            cancelText: "Cancel");

        if (confirmed != true)
        {
            return;
        }

        _isBusy = true;

        try
        {
            _result = await Store.RestoreAsync(_selected.Id, reload: true, await User.GetNameAsync());

            await LoadAsync();
        }
        finally
        {
            _isBusy = false;
        }
    }

    /// <summary>The diff, with long runs of unchanged lines folded away.</summary>
    private List<DiffRow> Rows()
    {
        List<DiffRow> rows = [];

        if (_diff is null)
        {
            return rows;
        }

        IReadOnlyList<DiffLine> lines = _diff.Lines;

        bool[] keep = new bool[lines.Count];

        for (int index = 0; index < lines.Count; index++)
        {
            if (lines[index].Kind == DiffKind.Same)
            {
                continue;
            }

            for (int near = Math.Max(0, index - ContextLines);
                 near <= Math.Min(lines.Count - 1, index + ContextLines);
                 near++)
            {
                keep[near] = true;
            }
        }

        int skipped = 0;

        for (int index = 0; index < lines.Count; index++)
        {
            if (keep[index] || !_diff.HasChanges)
            {
                if (skipped > 0)
                {
                    rows.Add(new DiffRow(null, skipped));
                    skipped = 0;
                }

                rows.Add(new DiffRow(lines[index], 0));
            }
            else
            {
                skipped++;
            }
        }

        if (skipped > 0)
        {
            rows.Add(new DiffRow(null, skipped));
        }

        return rows;
    }

    private static string AreaTitle(string area)
    {
        return Enum.TryParse(area, out ConfigArea parsed) && Enum.IsDefined(parsed) ? AreaInfo.For(parsed).Title : area;
    }

    private static string Describe(string reason)
    {
        return reason switch
        {
            "save" => "a change",
            "delete" => "a deletion",
            "restore" => "a restore",
            _ => reason,
        };
    }

    private static string FileUrl(ConfigBackup backup)
    {
        return Enum.TryParse(backup.Area, out ConfigArea parsed) && Enum.IsDefined(parsed)
            ? AreaInfo.For(parsed).EditUrl(backup.Name)
            : "/";
    }
}
