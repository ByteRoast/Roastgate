namespace Roastgate.Core.Nginx;

/// <param name="Wizard">The file carries the wizard's header, so the wizard can reopen it.</param>
public sealed record ConfigEntry(
    string Name,
    ConfigArea Area,
    bool Enabled,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    bool Wizard = false
);
