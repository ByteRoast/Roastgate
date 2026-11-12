namespace Roastgate.Core.Nginx;

/// <param name="OldLine">1-based line number in the older text, null for an added line.</param>
/// <param name="NewLine">1-based line number in the newer text, null for a removed line.</param>
public sealed record DiffLine(DiffKind Kind, string Text, int? OldLine, int? NewLine);
