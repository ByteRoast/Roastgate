namespace Roastgate.Core.Nginx.Generation;

/// <param name="Spec">The settings the file was generated from. Null for <see cref="MetadataStatus.None"/> and <see cref="MetadataStatus.Unreadable"/>.</param>
public sealed record SiteMetadata(MetadataStatus Status, SiteSpec? Spec);
