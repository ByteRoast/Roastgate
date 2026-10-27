namespace Roastgate.Core.Nginx.Generation;

/// <param name="Problems">
/// What is wrong with the spec. When empty, <paramref name="Text"/> holds the generated config.
/// </param>
/// <param name="NginxVersion">
/// The nginx version the config was generated for, or null when it could not be detected.
/// </param>
public sealed record WizardPreview(IReadOnlyList<SpecProblem> Problems, string? Text, string? NginxVersion = null);
