namespace Roastgate.Core.Nginx.Generation;

/// <param name="File">The file as it is on disk.</param>
/// <param name="Metadata">What the wizard knows about it.</param>
public sealed record WizardFile(ConfigFile File, SiteMetadata Metadata);
