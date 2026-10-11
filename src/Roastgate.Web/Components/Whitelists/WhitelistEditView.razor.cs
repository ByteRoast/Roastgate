using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using Roastgate.Core.Nginx;
using Roastgate.Core.Nginx.Whitelists;
using Roastgate.Web.Services;

namespace Roastgate.Web.Components.Whitelists;

public partial class WhitelistEditView
{
    [Inject] private IWhitelistService Whitelists { get; set; } = null!;
    [Inject] private CurrentUser User { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    private sealed class EntryRow
    {
        public string Address { get; set; } = string.Empty;

        public string Comment { get; set; } = string.Empty;
    }

    [Parameter]
    public string? Name { get; set; }

    // Asked from the browser once the page is interactive (the server-side HttpContext only exists during prerendering).
    private string? ClientAddress { get; set; }
    private readonly List<EntryRow> _rows = [];
    private List<string> _unsupported = [];
    private WhitelistDetails? _details;
    private ConfigResult? _result;
    private string _name = string.Empty;
    private string? _header;
    private string _paste = string.Empty;
    private bool _isNew;
    private bool _notFound;
    private bool _isBusy;
    private bool _reload = true;
    private bool _copied;
    private string? _loadedName;
    private bool _loaded;

    private bool HasBlockingError => CurrentProblems().Any((WhitelistProblem problem) => problem.Severity == ProblemSeverity.Error)
                                     || (_isNew && string.IsNullOrWhiteSpace(_name));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            string address = await JS.InvokeAsync<string>("roastgateClientAddress");
            if (!string.IsNullOrWhiteSpace(address))
            {
                ClientAddress = address;
                StateHasChanged();
            }
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loaded && _loadedName == Name)
        {
            return;
        }

        _loaded = true;
        _loadedName = Name;
        _isNew = string.IsNullOrWhiteSpace(Name);
        _rows.Clear();
        _unsupported = [];
        if (_isNew)
        {
            _name = string.Empty;
            AddRow();
            return;
        }

        _details = await Whitelists.ReadAsync(Name!);
        if (_details is null)
        {
            _notFound = true;
            return;
        }

        _name = _details.Name;
        ApplyDetails();
    }

    private void ApplyDetails()
    {
        if (_details?.Parse.Document is not { } document)
        {
            _unsupported = _details?.Parse.Unsupported.ToList() ?? [];
            return;
        }

        _header = document.Header;
        _rows.Clear();
        foreach (WhitelistEntry entry in document.Entries)
        {
            _rows.Add(new EntryRow { Address = entry.Address, Comment = entry.Comment ?? string.Empty });
        }
    }

    private IReadOnlyList<WhitelistProblem> CurrentProblems()
    {
        return WhitelistValidator.Validate(ToEntries());
    }

    private IReadOnlyList<WhitelistProblem> ProblemsFor(int position)
    {
        // Entries are validated as a whole (duplicates depend on the other rows); map the result back to this row.
        List<EntryRow> filled = _rows.Where((EntryRow row) => !IsBlank(row)).ToList();
        int filledIndex = filled.IndexOf(_rows[position]);
        return IsBlank(_rows[position])
            ? []
            : WhitelistValidator.Validate(ToEntries()).Where((WhitelistProblem problem) => problem.EntryIndex == filledIndex).ToList();
    }

    private List<WhitelistEntry> ToEntries()
    {
        return _rows
            .Where((EntryRow row) => !IsBlank(row))
            .Select((EntryRow row) => new WhitelistEntry(row.Address.Trim(), string.IsNullOrWhiteSpace(row.Comment) ? null : row.Comment.Trim()))
            .ToList();
    }

    private static bool IsBlank(EntryRow row)
    {
        return string.IsNullOrWhiteSpace(row.Address) && string.IsNullOrWhiteSpace(row.Comment);
    }

    private void AddRow()
    {
        _rows.Add(new EntryRow());
    }

    private void RemoveRow(int position)
    {
        _rows.RemoveAt(position);
    }

    private void AddMyAddress()
    {
        if (string.IsNullOrEmpty(ClientAddress))
        {
            return;
        }

        RemoveTrailingBlankRows();
        _rows.Add(new EntryRow { Address = ClientAddress, Comment = "my address" });
    }

    private void AddPasted()
    {
        RemoveTrailingBlankRows();
        string[] tokens = _paste.Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in tokens)
        {
            if (!token.Equals("allow", StringComparison.OrdinalIgnoreCase))
            {
                _rows.Add(new EntryRow { Address = token });
            }
        }

        _paste = string.Empty;
    }

    private void RemoveTrailingBlankRows()
    {
        while (_rows.Count > 0 && IsBlank(_rows[^1]))
        {
            _rows.RemoveAt(_rows.Count - 1);
        }
    }

    private static string UsageUrl(WhitelistUsage usage)
    {
        string route = usage.Area == ConfigArea.Sites ? "/sites/edit" : "/reverse-proxies/edit";
        return route + "?name=" + Uri.EscapeDataString(usage.FileName);
    }

    private async Task CopySnippetAsync()
    {
        if (_details is null)
        {
            return;
        }

        _copied = await JS.InvokeAsync<bool>("roastgateCopy", _details.IncludeSnippet + "\ndeny all;");
    }

    private async Task SaveAsync()
    {
        _isBusy = true;
        try
        {
            string actor = await User.GetNameAsync();
            ConfigResult result = await Whitelists.SaveAsync(_name, new WhitelistDocument(_header, ToEntries()), _reload, actor);
            _result = result;
            if (!result.Succeeded)
            {
                return;
            }

            string? normalized = ConfigNames.Normalize(ConfigArea.Whitelists, _name);
            if (normalized is null)
            {
                return;
            }

            _details = await Whitelists.ReadAsync(normalized);
            if (_isNew)
            {
                _isNew = false;
                _name = normalized;
                _loadedName = normalized;
                Navigation.NavigateTo("/whitelists/edit?name=" + Uri.EscapeDataString(normalized), replace: true);
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task DeleteAsync()
    {
        bool? confirmed = await Dialogs.ShowMessageBoxAsync(
            "Delete " + _name + "?",
            "The file is removed and nginx is reloaded. A backup of its content is kept.",
            yesText: "Delete",
            cancelText: "Cancel");
        if (confirmed != true)
        {
            return;
        }

        _isBusy = true;
        try
        {
            ConfigResult result = await Whitelists.DeleteAsync(_name, _reload, await User.GetNameAsync());
            if (result.Succeeded)
            {
                Navigation.NavigateTo("/whitelists");
                return;
            }

            _result = result;
        }
        finally
        {
            _isBusy = false;
        }
    }
}
