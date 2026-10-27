namespace Roastgate.Core.Nginx.Whitelists;

public enum ProblemSeverity
{
    /// <summary>nginx accepts it, but it is probably not what was meant.</summary>
    Warning,

    /// <summary>nginx would refuse it.</summary>
    Error,
}
