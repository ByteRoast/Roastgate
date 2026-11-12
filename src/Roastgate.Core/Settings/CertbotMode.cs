namespace Roastgate.Core.Settings;

public enum CertbotMode
{
    /// <summary>Use the certbot shipped inside the Roastgate image.</summary>
    Embedded,

    /// <summary>Use the certbot installed on the host (through HostExec).</summary>
    Host,

    Disabled,
}
