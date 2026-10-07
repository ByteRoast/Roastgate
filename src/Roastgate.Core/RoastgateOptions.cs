namespace Roastgate.Core;

/// <summary>How commands that target the host (nginx, certbot) are executed.</summary>
public enum HostExecMode
{
    /// <summary>Wrap every command in <c>nsenter</c> so it runs in the host namespaces (production, privileged container with pid: host).</summary>
    Nsenter,

    /// <summary>Run commands directly in the current environment (development and tests).</summary>
    Direct,
}

/// <summary>Process-level configuration, read once from <c>ROASTGATE_*</c> environment variables.</summary>
public sealed class RoastgateOptions
{
    public const string DataDirectoryVariable = "ROASTGATE_DATA_DIR";
    public const string HostExecVariable = "ROASTGATE_HOST_EXEC";

    /// <summary>Directory holding <c>roastgate.db</c>.</summary>
    public string DataDirectory { get; set; } = OperatingSystem.IsWindows()
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : "/data";

    public HostExecMode HostExecMode { get; set; } = OperatingSystem.IsWindows()
        ? HostExecMode.Direct
        : HostExecMode.Nsenter;

    public string DatabasePath => Path.Combine(DataDirectory, "roastgate.db");

    public static RoastgateOptions FromEnvironment()
    {
        RoastgateOptions options = new();

        string? dataDirectory = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            options.DataDirectory = dataDirectory;
        }

        string? hostExec = Environment.GetEnvironmentVariable(HostExecVariable);
        if (!string.IsNullOrWhiteSpace(hostExec))
        {
            if (!Enum.TryParse(hostExec, ignoreCase: true, out HostExecMode mode))
            {
                throw new InvalidOperationException(
                    $"{HostExecVariable} must be 'nsenter' or 'direct', got '{hostExec}'.");
            }

            options.HostExecMode = mode;
        }

        return options;
    }
}
