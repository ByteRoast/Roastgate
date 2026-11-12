namespace Roastgate.Core.Security;

public sealed record PasskeyCeremony(
    PasskeyCeremonyKind Kind,
    string OptionsJson,
    string? Ticket,
    DateTimeOffset ExpiresAt
);
