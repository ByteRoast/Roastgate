namespace Roastgate.Core.Nginx;

/// <summary>
/// A reverse proxy or a site, with its lines, as seen by the scanners that look for includes and certificates.
/// </summary>
/// <param name="Enabled">False for a disabled proxy or a site that is not enabled: nginx does not read it.</param>
public sealed record ManagedConfig(ConfigArea Area, string FileName, bool Enabled, string[] Lines);
