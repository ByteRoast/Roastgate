namespace Roastgate.Core.Nginx.Modules;

/// <summary>The optional directives (<see cref="KnownModule.OptionalDirectives"/>) the host's nginx accepts.</summary>
/// <param name="Known">
/// False when nginx could not be asked. A missing directive then means "not tested", not "refused": nginx has the last
/// word when the configuration is saved.
/// </param>
/// <param name="Supported">
/// The accepted directives, by <see cref="KnownModule.Id"/>. A module that is not loaded has no entry.
/// </param>
public sealed record ModuleDirectives(bool Known, IReadOnlyDictionary<string, IReadOnlySet<string>> Supported)
{
    /// <summary>
    /// What the probe answers when nginx could not be asked (nginx missing, configuration refused, timeout).
    /// </summary>
    public static ModuleDirectives Unknown { get; } = new(
        false,
        new Dictionary<string, IReadOnlySet<string>>()
    );

    /// <summary>
    /// Whether nginx accepts a directive of a module. False as well when the answer is unknown, so check
    /// <see cref="Known"/> first before telling someone the directive is not supported.
    /// </summary>
    public bool Supports(KnownModule module, string directive)
    {
        if (!Known)
        {
            return false;
        }

        return Supported.TryGetValue(module.Id, out IReadOnlySet<string>? directives) &&
               directives.Contains(directive);
    }
}
