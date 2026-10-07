using System.Text;
using Roastgate.Core.Data;
using Roastgate.Core.Nginx.Generation;
using Roastgate.Core.Settings;

namespace Roastgate.Core.Nginx;

public interface IConfigStore
{
    Task<ConfigListing> ListAsync(ConfigArea area, CancellationToken cancellationToken = default);

    Task<ConfigFile?> ReadAsync(ConfigArea area, string name, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a file: backup, atomic write, <c>nginx -t</c>, rollback if invalid, reload if asked.</summary>
    Task<ConfigResult> SaveAsync(ConfigArea area, string name, string content, bool reload, string actor, CancellationToken cancellationToken = default);

    Task<ConfigResult> DeleteAsync(ConfigArea area, string name, bool reload, string actor, CancellationToken cancellationToken = default);

    /// <summary>Puts the content of a backup back, through the same cycle as a save. What is replaced is backed up in turn.</summary>
    Task<ConfigResult> RestoreAsync(long backupId, bool reload, string actor, CancellationToken cancellationToken = default);

    Task<ConfigResult> SetEnabledAsync(ConfigArea area, string name, bool enabled, bool reload, string actor, CancellationToken cancellationToken = default);

    Task<ConfigResult> TestNginxAsync(string actor, CancellationToken cancellationToken = default);

    Task<ConfigResult> ReloadNginxAsync(string actor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads and changes the nginx files. Every change goes through the same cycle, serialized by one lock:
/// backup, write, <c>nginx -t</c>, automatic rollback if nginx refuses it, reload if requested.
/// </summary>
public sealed class ConfigStore(
    AppSettingsService settingsService,
    INginxController nginx,
    IConfigBackupStore backups,
    IAuditLog audit) : IConfigStore
{
    private const string TemporarySuffix = ".tmp-roastgate";
    private const int MaximumAuditDetailLength = 2000;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    // ---- Reading ------------------------------------------------------------------------

    public async Task<ConfigListing> ListAsync(ConfigArea area, CancellationToken cancellationToken = default)
    {
        ConfigLocations locations = await GetLocationsAsync(cancellationToken);
        string directory = locations.Directory(area);
        if (!Directory.Exists(directory))
        {
            return new ConfigListing([], directory, false);
        }

        Dictionary<string, ConfigEntry> entries = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            string fileName = Path.GetFileName(path);
            bool disabledFile = area == ConfigArea.ReverseProxies && fileName.EndsWith(ConfigNames.ConfigSuffix + ConfigNames.DisabledSuffix, StringComparison.Ordinal);
            string name = disabledFile ? fileName[..^ConfigNames.DisabledSuffix.Length] : fileName;
            if (!ConfigNames.IsValid(name) || (area != ConfigArea.Sites && !name.EndsWith(ConfigNames.ConfigSuffix, StringComparison.Ordinal)))
            {
                continue;
            }

            // When a.conf and a.conf.disabled both exist, the enabled one is what nginx reads.
            if (entries.ContainsKey(name) && disabledFile)
            {
                continue;
            }

            FileInfo info = new(path);
            bool enabled = area switch
            {
                ConfigArea.ReverseProxies => !disabledFile,
                ConfigArea.Sites => IsLinkedTo(Path.Combine(locations.SitesEnabledDirectory, name), path),
                _ => true,
            };
            entries[name] = new ConfigEntry(name, area, enabled, info.Length, info.LastWriteTimeUtc, IsWizardFile(path));
        }

        List<ConfigEntry> sorted = entries.Values.OrderBy((ConfigEntry entry) => entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new ConfigListing(sorted, directory, true);
    }

    public async Task<ConfigFile?> ReadAsync(ConfigArea area, string name, CancellationToken cancellationToken = default)
    {
        string? normalized = ConfigNames.Normalize(area, name);
        if (normalized is null)
        {
            return null;
        }

        ConfigLocations locations = await GetLocationsAsync(cancellationToken);
        string? path = Locate(locations, area, normalized, out bool disabledFile);
        if (path is null)
        {
            return null;
        }

        FileInfo info = new(path);
        if (info.Length > ConfigNames.MaximumContentBytes)
        {
            return null;
        }

        string content = await File.ReadAllTextAsync(path, cancellationToken);
        bool enabled = area switch
        {
            ConfigArea.ReverseProxies => !disabledFile,
            ConfigArea.Sites => IsLinkedTo(Path.Combine(locations.SitesEnabledDirectory, normalized), path),
            _ => true,
        };
        return new ConfigFile(normalized, area, content, enabled, info.LastWriteTimeUtc);
    }

    // ---- Changing -----------------------------------------------------------------------

    public Task<ConfigResult> SaveAsync(
        ConfigArea area,
        string name,
        string content,
        bool reload,
        string actor,
        CancellationToken cancellationToken = default)
    {
        return SaveCoreAsync(area, name, content, reload, actor, "save", "config.save", cancellationToken);
    }

    public async Task<ConfigResult> RestoreAsync(long backupId, bool reload, string actor, CancellationToken cancellationToken = default)
    {
        ConfigBackup? backup = await backups.GetAsync(backupId, cancellationToken);
        if (backup is null || !Enum.TryParse(backup.Area, out ConfigArea area) || !Enum.IsDefined(area))
        {
            return new ConfigResult(false, "That backup does not exist.");
        }

        return await SaveCoreAsync(area, backup.Name, backup.Content, reload, actor, "restore", "config.restore", cancellationToken);
    }

    private async Task<ConfigResult> SaveCoreAsync(
        ConfigArea area,
        string name,
        string content,
        bool reload,
        string actor,
        string backupReason,
        string auditAction,
        CancellationToken cancellationToken)
    {
        string? normalized = ConfigNames.Normalize(area, name);
        if (normalized is null)
        {
            return new ConfigResult(false, InvalidNameMessage(area));
        }

        string text = NormalizeContent(content);
        if (Encoding.UTF8.GetByteCount(text) > ConfigNames.MaximumContentBytes)
        {
            return new ConfigResult(false, "The file is larger than 1 MiB.");
        }

        ConfigLocations locations = await GetLocationsAsync(cancellationToken);
        string directory = locations.Directory(area);
        string? existing = Locate(locations, area, normalized, out _);
        string path = existing ?? Path.Combine(directory, normalized);
        string? previous = existing is null ? null : await File.ReadAllTextAsync(existing, cancellationToken);
        long? createdBackup = null;

        return await MutateAsync(
            actor,
            auditAction,
            Describe(area, normalized),
            reload,
            async () =>
            {
                if (previous is not null)
                {
                    createdBackup = await backups.AddAsync(area.ToString(), normalized, backupReason, previous, cancellationToken);
                }

                Directory.CreateDirectory(directory);
                await WriteAtomicAsync(path, text, cancellationToken);
            },
            async () =>
            {
                await DiscardBackupAsync(createdBackup);
                if (previous is null)
                {
                    File.Delete(path);
                }
                else
                {
                    await WriteAtomicAsync(path, previous, CancellationToken.None);
                }
            },
            backupReason == "restore" ? "Restored." : "Saved.",
            cancellationToken);
    }

    public async Task<ConfigResult> DeleteAsync(ConfigArea area, string name, bool reload, string actor, CancellationToken cancellationToken = default)
    {
        string? normalized = ConfigNames.Normalize(area, name);
        ConfigLocations locations = await GetLocationsAsync(cancellationToken);
        string? path = normalized is null ? null : Locate(locations, area, normalized, out _);
        if (normalized is null || path is null)
        {
            return new ConfigResult(false, "That file does not exist.");
        }

        string previous = await File.ReadAllTextAsync(path, cancellationToken);
        string linkPath = Path.Combine(locations.SitesEnabledDirectory, normalized);
        bool wasLinked = area == ConfigArea.Sites && IsLinkedTo(linkPath, path);
        long? deletionBackup = null;

        return await MutateAsync(
            actor,
            "config.delete",
            Describe(area, normalized),
            reload,
            async () =>
            {
                deletionBackup = await backups.AddAsync(area.ToString(), normalized, "delete", previous, cancellationToken);
                if (wasLinked)
                {
                    File.Delete(linkPath);
                }

                File.Delete(path);
            },
            async () =>
            {
                await DiscardBackupAsync(deletionBackup);
                await WriteAtomicAsync(path, previous, CancellationToken.None);
                if (wasLinked)
                {
                    CreateLink(linkPath, path);
                }
            },
            "Deleted. A backup was kept.",
            cancellationToken);
    }

    public async Task<ConfigResult> SetEnabledAsync(
        ConfigArea area,
        string name,
        bool enabled,
        bool reload,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (area == ConfigArea.Whitelists)
        {
            return new ConfigResult(false, "Whitelists are not enabled or disabled: they apply wherever they are included.");
        }

        string? normalized = ConfigNames.Normalize(area, name);
        ConfigLocations locations = await GetLocationsAsync(cancellationToken);
        string? path = normalized is null ? null : Locate(locations, area, normalized, out bool disabledFile);
        if (normalized is null || path is null)
        {
            return new ConfigResult(false, "That file does not exist.");
        }

        string action = enabled ? "config.enable" : "config.disable";
        if (area == ConfigArea.ReverseProxies)
        {
            string activePath = Path.Combine(locations.Directory(area), normalized);
            string disabledPath = activePath + ConfigNames.DisabledSuffix;
            bool isDisabled = path == disabledPath;
            if (isDisabled != enabled)
            {
                return new ConfigResult(true, enabled ? "Already enabled." : "Already disabled.");
            }

            string from = enabled ? disabledPath : activePath;
            string to = enabled ? activePath : disabledPath;
            return await MutateAsync(
                actor,
                action,
                Describe(area, normalized),
                reload,
                () =>
                {
                    File.Move(from, to);
                    return Task.CompletedTask;
                },
                () =>
                {
                    File.Move(to, from);
                    return Task.CompletedTask;
                },
                enabled ? "Enabled." : "Disabled.",
                cancellationToken);
        }

        string linkPath = Path.Combine(locations.SitesEnabledDirectory, normalized);
        bool linked = IsLinkedTo(linkPath, path);
        if (linked == enabled)
        {
            return new ConfigResult(true, enabled ? "Already enabled." : "Already disabled.");
        }

        // Only relevant when enabling: the name must not already be taken by something that is not our link.
        if (enabled && (File.Exists(linkPath) || new FileInfo(linkPath).LinkTarget is not null))
        {
            return new ConfigResult(
                false,
                $"{linkPath} exists but is not a symlink to this site, so Roastgate will not touch it. Remove or rename it by hand first.");
        }

        return await MutateAsync(
            actor,
            action,
            Describe(area, normalized),
            reload,
            () =>
            {
                if (enabled)
                {
                    Directory.CreateDirectory(locations.SitesEnabledDirectory);
                    CreateLink(linkPath, path);
                }
                else
                {
                    File.Delete(linkPath);
                }

                return Task.CompletedTask;
            },
            () =>
            {
                if (enabled)
                {
                    File.Delete(linkPath);
                }
                else
                {
                    CreateLink(linkPath, path);
                }

                return Task.CompletedTask;
            },
            enabled ? "Enabled." : "Disabled.",
            cancellationToken);
    }

    public async Task<ConfigResult> TestNginxAsync(string actor, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            NginxResult result = await nginx.TestAsync(cancellationToken);
            await audit.WriteAsync(actor, "nginx.test", null, result.Succeeded, Truncate(result.Output), cancellationToken);
            return result.Succeeded
                ? new ConfigResult(true, "The configuration is valid.", result.Output)
                : new ConfigResult(false, "nginx rejects the current configuration.", result.Output);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<ConfigResult> ReloadNginxAsync(string actor, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            NginxResult test = await nginx.TestAsync(cancellationToken);
            if (!test.Succeeded)
            {
                await audit.WriteAsync(actor, "nginx.reload", null, false, Truncate(test.Output), cancellationToken);
                return new ConfigResult(false, "nginx was not reloaded because the configuration is invalid.", test.Output);
            }

            NginxResult reload = await nginx.ReloadAsync(cancellationToken);
            await audit.WriteAsync(actor, "nginx.reload", null, reload.Succeeded, Truncate(reload.Output), cancellationToken);
            return reload.Succeeded
                ? new ConfigResult(true, "nginx reloaded.", reload.Output, Reloaded: true)
                : new ConfigResult(false, "nginx accepted the configuration but the reload failed.", reload.Output);
        }
        finally
        {
            Gate.Release();
        }
    }

    // ---- The change cycle ---------------------------------------------------------------

    private async Task<ConfigResult> MutateAsync(
        string actor,
        string action,
        string target,
        bool reload,
        Func<Task> apply,
        Func<Task> undo,
        string successMessage,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            ConfigResult result = await RunCycleAsync(apply, undo, reload, successMessage, cancellationToken);
            await audit.WriteAsync(actor, action, target, result.Succeeded, Truncate(result.NginxOutput ?? result.Message), cancellationToken);
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<ConfigResult> RunCycleAsync(
        Func<Task> apply,
        Func<Task> undo,
        bool reload,
        string successMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await apply();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await TryUndoAsync(undo);
            return new ConfigResult(false, $"The file system refused the change: {exception.Message}", RolledBack: true);
        }

        NginxResult test = await nginx.TestAsync(cancellationToken);
        if (!test.Succeeded)
        {
            await TryUndoAsync(undo);
            NginxResult baseline = await nginx.TestAsync(cancellationToken);
            string message = baseline.Succeeded
                ? "nginx rejected the change, so it was undone."
                : "nginx -t also fails without this change (the configuration is broken elsewhere), so the change was undone.";
            return new ConfigResult(false, message, test.Output, RolledBack: true);
        }

        if (!reload)
        {
            return new ConfigResult(true, successMessage + " nginx was not reloaded.", test.Output);
        }

        NginxResult reloaded = await nginx.ReloadAsync(cancellationToken);
        return reloaded.Succeeded
            ? new ConfigResult(true, successMessage + " nginx reloaded.", reloaded.Output, Reloaded: true)
            : new ConfigResult(true, successMessage + " The configuration is valid, but reloading nginx failed.", reloaded.Output, Warning: true);
    }

    private static async Task TryUndoAsync(Func<Task> undo)
    {
        try
        {
            await undo();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done here; the failed test result already tells the user what happened.
        }
    }

    // ---- File helpers -------------------------------------------------------------------

    /// <summary>A change that was undone leaves nothing to restore: its backup would only repeat the current file.</summary>
    private async Task DiscardBackupAsync(long? backupId)
    {
        if (backupId is { } id)
        {
            await backups.DeleteAsync(id, CancellationToken.None);
        }
    }

    private static bool IsWizardFile(string path)
    {
        try
        {
            using StreamReader reader = new(path);
            char[] buffer = new char[1024];
            int read = reader.Read(buffer, 0, buffer.Length);
            return SiteMetadataFormat.IsWizardFile(new string(buffer, 0, read));
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task<ConfigLocations> GetLocationsAsync(CancellationToken cancellationToken)
    {
        return new ConfigLocations(await settingsService.GetAsync(cancellationToken));
    }

    /// <summary>Finds the file for a name, whichever of its enabled or disabled forms exists.</summary>
    private static string? Locate(ConfigLocations locations, ConfigArea area, string name, out bool disabledFile)
    {
        string path = Path.Combine(locations.Directory(area), name);
        disabledFile = false;
        if (File.Exists(path))
        {
            return path;
        }

        if (area == ConfigArea.ReverseProxies && File.Exists(path + ConfigNames.DisabledSuffix))
        {
            disabledFile = true;
            return path + ConfigNames.DisabledSuffix;
        }

        return null;
    }

    private static string NormalizeContent(string content)
    {
        string text = content.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
        return text.Length == 0 ? string.Empty : text + "\n";
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        string temporary = path + TemporarySuffix;
        try
        {
            await File.WriteAllTextAsync(temporary, content, Utf8WithoutBom, cancellationToken);
            if (!OperatingSystem.IsWindows() && File.Exists(path))
            {
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>True when <paramref name="linkPath"/> is a symbolic link that resolves to <paramref name="file"/>.</summary>
    internal static bool IsLinkedTo(string linkPath, string file)
    {
        FileInfo link = new(linkPath);
        if (link.LinkTarget is null)
        {
            return false;
        }

        FileSystemInfo? resolved = link.ResolveLinkTarget(returnFinalTarget: true);
        if (resolved is null)
        {
            return false;
        }

        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(resolved.FullName), Path.GetFullPath(file), comparison);
    }

    private static void CreateLink(string linkPath, string file)
    {
        string relativeTarget = Path.GetRelativePath(Path.GetDirectoryName(linkPath)!, file);
        File.CreateSymbolicLink(linkPath, relativeTarget);
    }

    private static string Describe(ConfigArea area, string name)
    {
        string directory = area switch
        {
            ConfigArea.ReverseProxies => "conf.d",
            ConfigArea.Sites => "sites-available",
            _ => "whitelists",
        };
        return directory + "/" + name;
    }

    private static string InvalidNameMessage(ConfigArea area)
    {
        string suffix = area == ConfigArea.Sites ? string.Empty : " (the .conf extension is added when missing)";
        return "The name may only contain letters, digits, dots, hyphens and underscores, must start with a letter or digit, and be at most "
               + ConfigNames.MaximumLength + " characters" + suffix + ".";
    }

    private static string? Truncate(string? text)
    {
        if (text is null || text.Length <= MaximumAuditDetailLength)
        {
            return text;
        }

        return text[..MaximumAuditDetailLength] + "...";
    }
}
