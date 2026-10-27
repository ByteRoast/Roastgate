using Roastgate.Core.Hosting;

namespace Roastgate.Core.Nginx.Modules;

/// <summary>An nginx module Roastgate knows how to configure.</summary>
/// <param name="Id">The stable identifier stored in site specs (for example fancyindex).</param>
/// <param name="DisplayName">The name shown in the wizard.</param>
/// <param name="SharedObjectName">The file name <c>load_module</c> points to, as <see cref="NginxModules"/> lists it.</param>
public sealed record KnownModule(string Id, string DisplayName, string SharedObjectName)
{
    /// <summary>Directory listings for static sites (ngx_http_fancyindex_module).</summary>
    public static KnownModule FancyIndex { get; } = new(
        "fancyindex",
        "Fancy Index",
        "ngx_http_fancyindex_module.so"
    );

    /// <summary>
    /// Whether nginx has loaded this module. False as well when the loaded modules are unknown, so check
    /// <see cref="NginxModules.Known"/> first before telling someone the module is missing.
    /// </summary>
    public bool IsLoaded(NginxModules modules)
    {
        return modules.Has(SharedObjectName);
    }
}
