namespace Roastgate.Core.Nginx.Modules;

/// <summary>
/// The minimal configuration the capability probe hands to <c>nginx -t -c</c> to learn whether a build of a module
/// accepts some directives.
/// </summary>
public static class DirectiveProbeConfiguration
{
    /// <summary>
    /// Loads the modules as the host's configuration does, then writes each directive in a block of its context. The
    /// <c>events</c> block is required by nginx; the other blocks are written even when empty.
    /// </summary>
    /// <param name="loadPaths">The <see cref="Hosting.NginxModules.LoadPaths"/>, in the order nginx loads them.</param>
    /// <param name="directives">The directives to test, each with a value nginx accepts.</param>
    public static string Build(IReadOnlyList<string> loadPaths, IReadOnlyList<ModuleDirective> directives)
    {
        NginxWriter writer = new();

        foreach (string path in loadPaths)
        {
            writer.Line($"load_module {path};");
        }

        writer.Blank();
        writer.Open("events");
        writer.Close();
        writer.Blank();
        writer.Open("http");

        foreach (ModuleDirective directive in GetContextDirectives(directives, DirectiveContext.Http))
        {
            writer.Line($"{directive.Name} {directive.SampleValue};");
        }

        writer.Blank();
        writer.Open("server");

        foreach (ModuleDirective directive in GetContextDirectives(directives, DirectiveContext.Server))
        {
            writer.Line($"{directive.Name} {directive.SampleValue};");
        }

        writer.Blank();
        writer.Open("location /");

        foreach (ModuleDirective directive in GetContextDirectives(directives, DirectiveContext.Location))
        {
            writer.Line($"{directive.Name} {directive.SampleValue};");
        }

        writer.Close(); // Closes "location /" block
        writer.Close(); // Closes "server" block
        writer.Close(); // Closes "http" block

        return writer.ToString();
    }

    private static IEnumerable<ModuleDirective> GetContextDirectives(
        IReadOnlyList<ModuleDirective> directives,
        DirectiveContext context
    )
    {
        return directives.Where(d => d.Context == context);
    }
}
