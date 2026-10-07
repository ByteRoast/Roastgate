using Roastgate.Core.Settings;

namespace Roastgate.Core.Nginx.Whitelists;

public interface IWhitelistService
{
    Task<IReadOnlyList<WhitelistSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<WhitelistDetails?> ReadAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Validates the entries, then saves through the usual backup, test, rollback and reload cycle.</summary>
    Task<ConfigResult> SaveAsync(string name, WhitelistDocument document, bool reload, string actor, CancellationToken cancellationToken = default);

    /// <summary>Refuses while a config includes the whitelist; otherwise deletes it (nginx -t is still the last safety net).</summary>
    Task<ConfigResult> DeleteAsync(string name, bool reload, string actor, CancellationToken cancellationToken = default);
}

public sealed class WhitelistService(
    IConfigStore store,
    IWhitelistUsageScanner scanner,
    AppSettingsService settingsService) : IWhitelistService
{
    public async Task<IReadOnlyList<WhitelistSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        ConfigListing listing = await store.ListAsync(ConfigArea.Whitelists, cancellationToken);
        List<string> names = listing.Entries.Select((ConfigEntry entry) => entry.Name).ToList();
        IReadOnlyDictionary<string, IReadOnlyList<WhitelistUsage>> usage = await scanner.FindAsync(names, cancellationToken);

        List<WhitelistSummary> summaries = [];
        foreach (ConfigEntry entry in listing.Entries)
        {
            ConfigFile? file = await store.ReadAsync(ConfigArea.Whitelists, entry.Name, cancellationToken);
            int? count = file is null ? null : WhitelistFormat.Parse(file.Content).Document?.Entries.Count;
            summaries.Add(new WhitelistSummary(entry.Name, count, usage[entry.Name], entry.ModifiedAt));
        }

        return summaries;
    }

    public async Task<WhitelistDetails?> ReadAsync(string name, CancellationToken cancellationToken = default)
    {
        ConfigFile? file = await store.ReadAsync(ConfigArea.Whitelists, name, cancellationToken);
        if (file is null)
        {
            return null;
        }

        IReadOnlyDictionary<string, IReadOnlyList<WhitelistUsage>> usage = await scanner.FindAsync([file.Name], cancellationToken);
        ConfigLocations locations = new(await settingsService.GetAsync(cancellationToken));
        return new WhitelistDetails(
            file.Name,
            file.Content,
            WhitelistFormat.Parse(file.Content),
            usage[file.Name],
            $"include {locations.WhitelistIncludePath(file.Name)};",
            file.ModifiedAt);
    }

    public Task<ConfigResult> SaveAsync(string name, WhitelistDocument document, bool reload, string actor, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WhitelistProblem> errors = WhitelistValidator.Validate(document.Entries)
            .Where((WhitelistProblem problem) => problem.Severity == ProblemSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            string message = "Nothing was saved: " + string.Join(" ", errors.Select((WhitelistProblem problem) => problem.Message));
            return Task.FromResult(new ConfigResult(false, message));
        }

        return store.SaveAsync(ConfigArea.Whitelists, name, WhitelistFormat.Write(document), reload, actor, cancellationToken);
    }

    public async Task<ConfigResult> DeleteAsync(string name, bool reload, string actor, CancellationToken cancellationToken = default)
    {
        string? normalized = ConfigNames.Normalize(ConfigArea.Whitelists, name);
        if (normalized is null)
        {
            return new ConfigResult(false, "That file does not exist.");
        }

        IReadOnlyDictionary<string, IReadOnlyList<WhitelistUsage>> usage = await scanner.FindAsync([normalized], cancellationToken);
        IReadOnlyList<WhitelistUsage> users = usage[normalized];
        if (users.Count > 0)
        {
            string list = string.Join(", ", users.Select((WhitelistUsage user) => user.FileName).Distinct());
            return new ConfigResult(false, $"{normalized} is still included by {list}. Remove those includes first.");
        }

        return await store.DeleteAsync(ConfigArea.Whitelists, normalized, reload, actor, cancellationToken);
    }
}
