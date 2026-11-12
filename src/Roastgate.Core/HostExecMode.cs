namespace Roastgate.Core;

/// <summary>How commands that target the host (nginx, certbot) are executed.</summary>
public enum HostExecMode
{
    /// <summary>
    /// Wrap every command in <c>nsenter</c> so it runs in the host namespaces (production, privileged container with
    /// pid: host).
    /// </summary>
    Nsenter,

    /// <summary>Run commands directly in the current environment (development and tests).</summary>
    Direct,
}
