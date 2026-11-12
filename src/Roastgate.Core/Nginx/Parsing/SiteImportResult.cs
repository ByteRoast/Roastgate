using Roastgate.Core.Nginx.Generation;

namespace Roastgate.Core.Nginx.Parsing;

/// <param name="Spec">
/// The settings read from the file, or null when it cannot be imported without changing what it does.
/// </param>
/// <param name="Notes">
/// Things the user should know about the import (the wizard adds some defaults, comments are dropped...).
/// </param>
/// <param name="Blockers">Why the file cannot be imported. Empty when <paramref name="Spec"/> is set.</param>
public sealed record SiteImportResult(SiteSpec? Spec, IReadOnlyList<string> Notes, IReadOnlyList<string> Blockers)
{
    public bool Succeeded => Spec is not null;
}
