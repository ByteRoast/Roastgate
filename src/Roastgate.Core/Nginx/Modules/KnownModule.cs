using Roastgate.Core.Hosting;

namespace Roastgate.Core.Nginx.Modules;

/// <summary>An nginx module Roastgate knows how to configure.</summary>
/// <param name="Id">The stable identifier stored in site specs (for example fancyindex).</param>
/// <param name="DisplayName">The name shown in the wizard.</param>
/// <param name="SharedObjectName">The file name <c>load_module</c> points to, as <see cref="NginxModules"/> lists it.</param>
public sealed record KnownModule(string Id, string DisplayName, string SharedObjectName)
{
    /// <summary>
    /// Directives that some builds of the module do not have. The capability probe asks nginx about each one before
    /// Roastgate writes it, since nginx refuses a whole file that names an unknown directive.
    /// </summary>
    public IReadOnlyList<ModuleDirective> OptionalDirectives { get; init; } = [];

    /// <summary>Directory listings for static sites (ngx_http_fancyindex_module).</summary>
    public static KnownModule FancyIndex { get; } = new(
        "fancyindex",
        "Fancy Index",
        "ngx_http_fancyindex_module.so"
    )
    {
        OptionalDirectives =
        [
            new ModuleDirective("fancyindex_case_sensitive", DirectiveContext.Location, "on")
        ]
    };

    /// <summary>
    /// Every module of the catalog. Declared after the entries it lists: static initializers run in source order, so an
    /// entry declared below would still be null here.
    /// </summary>
    public static IReadOnlyList<KnownModule> All { get; } = [FancyIndex];

    /// <summary>
    /// Whether nginx has loaded this module. False as well when the loaded modules are unknown, so check
    /// <see cref="NginxModules.Known"/> first before telling someone the module is missing.
    /// </summary>
    public bool IsLoaded(NginxModules modules)
    {
        return modules.Has(SharedObjectName);
    }
}
