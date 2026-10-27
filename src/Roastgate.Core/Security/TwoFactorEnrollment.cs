namespace Roastgate.Core.Security;

/// <summary>
/// What the user scans (the QR code carries <see cref="ProvisioningUri"/>) or types in (<see cref="Secret"/>).
/// </summary>
public sealed record TwoFactorEnrollment(string Secret, string ProvisioningUri);
