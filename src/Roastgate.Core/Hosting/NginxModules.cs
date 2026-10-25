using System.Text.RegularExpressions;

namespace Roastgate.Core.Hosting;

/// <summary>
/// The dynamic modules nginx has loaded, as shared object file names (for example ngx_http_fancyindex_module.so).
/// </summary>
/// <param name="Known">
/// False when nginx could not be asked. "No module loaded" is then not a valid answer, unlike an empty <paramref name="Modules"/> when this is true.
/// </param>
public sealed partial record NginxModules(bool Known, IReadOnlySet<string> Modules)
{
    [GeneratedRegex("""^\s*load_module\s+"?(?<path>[^";\s]+)"?\s*;""", RegexOptions.Multiline)]
    private static partial Regex LoadModulePattern();

    /// <summary>
    /// What the host answered when nginx could not be asked (nginx missing, configuration refused, timeout).
    /// </summary>
    public static NginxModules Unknown { get; } = new(false, new HashSet<string>());

    /// <summary>
    /// What the host answered when nginx could not be asked (nginx missing, configuration refused, timeout).
    /// </summary>
    public static NginxModules Parse(string output)
    {
        HashSet<string> modules = new(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in LoadModulePattern().Matches(output))
        {
            string path = match.Groups["path"].Value;
            string name = Path.GetFileName(path);

            modules.Add(name);
        }

        return new NginxModules(true, modules);
    }

    /// <summary>
    /// Whether a shared object is loaded. Always false when <see cref="Known"/> is false: check that first before concluding a module is missing.
    /// </summary>
    public bool Has(string sharedObject)
    {
        return Known && Modules.Contains(sharedObject);
    }
}
