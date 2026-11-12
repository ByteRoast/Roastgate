namespace Roastgate.Core.Security;

public enum PasswordVerification
{
    Failed,
    Success,

    /// <summary>The password is correct but was hashed with fewer iterations than the current default.</summary>
    SuccessRehashNeeded,
}
