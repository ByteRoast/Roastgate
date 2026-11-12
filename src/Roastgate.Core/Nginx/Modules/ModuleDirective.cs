namespace Roastgate.Core.Nginx.Modules;

/// <param name="SampleValue">
/// A value nginx accepts for this directive. An invalid one makes nginx answer "invalid value" instead of "unknown
/// directive", and the probe learns nothing.
/// </param>
public sealed record ModuleDirective(string Name, DirectiveContext Context, string SampleValue);
