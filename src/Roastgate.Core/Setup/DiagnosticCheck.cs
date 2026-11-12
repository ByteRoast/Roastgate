namespace Roastgate.Core.Setup;

public sealed record DiagnosticCheck(string Name, DiagnosticStatus Status, string Message);
