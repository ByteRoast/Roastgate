namespace Roastgate.Core.Nginx.Generation;

/// <param name="Field">The <see cref="SiteSpec"/> property the message is about.</param>
public sealed record SpecProblem(string Field, string Message);
