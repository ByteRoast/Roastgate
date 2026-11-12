using Roastgate.Core.Hosting;

namespace Roastgate.Core.Nginx.Modules;

/// <summary>
/// Asks the host's nginx which <see cref="KnownModule.OptionalDirectives"/> its build of each loaded module accepts,
/// by testing a temporary configuration with <c>nginx -t -c</c>.
/// </summary>
public sealed class ModuleDirectiveProbe(IHostExec hostExec)
{
    /// <summary>
    /// Tests every optional directive of the loaded modules in one configuration, and removes each one nginx reports as
    /// unknown until the rest passes. Unknown when the loaded modules are, when nginx fails for another reason, or when
    /// the temporary file cannot be written.
    /// </summary>
    /// <param name="modules">
    /// The loaded modules, whose <c>load_module</c> lines are copied into the configuration.
    /// </param>
    /// <param name="probeDirectory">
    /// Where to write the temporary configuration. It must be at the same path for Roastgate and for nginx on the host
    /// (a folder of the nginx root, which the container mounts at the same path). The file is deleted afterwards.
    /// </param>
    /// <param name="cancellationToken">Stops the probe; the temporary file is still deleted.</param>
    public async Task<ModuleDirectives> ProbeAsync(
        NginxModules modules,
        string probeDirectory,
        CancellationToken cancellationToken
    )
    {
        if (!modules.Known)
        {
            return ModuleDirectives.Unknown;
        }

        List<ModuleDirective> remaining = [];

        foreach (KnownModule module in KnownModule.All)
        {
            if (module.IsLoaded(modules))
            {
                remaining.AddRange(module.OptionalDirectives);
            }
        }

        if (remaining.Count == 0)
        {
            return Supported(modules, remaining);
        }

        string path = Path.Combine(probeDirectory, $"{Guid.NewGuid():N}.conf");

        try
        {
            Directory.CreateDirectory(probeDirectory);

            while (true)
            {
                string directiveContent = DirectiveProbeConfiguration.Build(modules.LoadPaths, remaining);

                await File.WriteAllTextAsync(path, directiveContent, cancellationToken);

                HostCommandResult result = await hostExec.RunAsync(
                    "nginx",
                    ["-t", "-c", path],
                    TimeSpan.FromSeconds(10),
                    cancellationToken
                );

                if (result.Succeeded)
                {
                    break;
                }

                string? name = UnknownDirectiveError.Find(result.CombinedOutput);

                if (name is null || remaining.RemoveAll(directive => directive.Name == name) == 0)
                {
                    return ModuleDirectives.Unknown;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ModuleDirectives.Unknown;
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Supported(modules, remaining);
    }

    /// <summary>The directives left after the probe, by module, with an entry for every loaded module.</summary>
    private static ModuleDirectives Supported(NginxModules modules, List<ModuleDirective> remaining)
    {
        Dictionary<string, IReadOnlySet<string>> supported = [];

        foreach (KnownModule module in KnownModule.All)
        {
            if (module.IsLoaded(modules))
            {
                supported.Add(
                    module.Id,
                    module.OptionalDirectives
                        .Where(remaining.Contains)
                        .Select(directive => directive.Name)
                        .ToHashSet(StringComparer.Ordinal)
                );
            }
        }

        return new ModuleDirectives(true, supported);
    }
}
