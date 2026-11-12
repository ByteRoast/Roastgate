namespace Roastgate.Core.Nginx;

/// <param name="Succeeded">The requested change is in place.</param>
/// <param name="Message">A sentence for the user.</param>
/// <param name="NginxOutput">What nginx printed, when it was involved.</param>
/// <param name="RolledBack">The change was refused by nginx and undone.</param>
/// <param name="Reloaded">nginx was asked to reload and accepted.</param>
/// <param name="Warning">
/// The change is in place but something needs the user's attention (for example the reload failed).
/// </param>
public sealed record ConfigResult(
    bool Succeeded,
    string Message,
    string? NginxOutput = null,
    bool RolledBack = false,
    bool Reloaded = false,
    bool Warning = false
);
