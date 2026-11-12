using System.Text.RegularExpressions;

namespace Roastgate.Core.Hosting;

/// <summary>
/// The dynamic modules nginx has loaded, as shared object file names (for example ngx_http_fancyindex_module.so).
/// </summary>
/// <param name="Modules">The shared object file names, used to tell whether a module is loaded.</param>
/// <param name="LoadPaths">
/// The paths of the <c>load_module</c> lines, as written and in the order nginx loads them, without duplicates. The
/// capability probe copies them into its test configuration: some modules need another one loaded first, and a relative
/// path still resolves against the same prefix with <c>nginx -t -c</c>.
/// </param>
public sealed partial record NginxModules(bool Known, IReadOnlySet<string> Modules, IReadOnlyList<string> LoadPaths)
{
    [GeneratedRegex("""^\s*load_module\s+"?(?<path>[^";\s]+)"?\s*;""", RegexOptions.Multiline)]
    private static partial Regex LoadModulePattern();

    /// <summary>
    /// What the host answered when nginx could not be asked (nginx missing, configuration refused, timeout).
    /// </summary>
    public static NginxModules Unknown { get; } = new(false, new HashSet<string>(), []);

    /// <summary>Reads the <c>load_module</c> lines of the output of <c>nginx -T</c>.</summary>
    public static NginxModules Parse(string output)
    {
        HashSet<string> modules = new(StringComparer.OrdinalIgnoreCase);
        List<string> paths = [];

        foreach (Match match in LoadModulePattern().Matches(output))
        {
            string path = match.Groups["path"].Value;
            string name = Path.GetFileName(path);

            if (!paths.Contains(path))
            {
                paths.Add(path);
            }

            modules.Add(name);
        }

        return new NginxModules(true, modules, paths);
    }

    /// <summary>
    /// Whether a shared object is loaded. Always false when <see cref="Known"/> is false: check that first before
    /// concluding a module is missing.
    /// </summary>
    public bool Has(string sharedObject)
    {
        return Known && Modules.Contains(sharedObject);
    }
}
