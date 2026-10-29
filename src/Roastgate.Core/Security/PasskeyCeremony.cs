using System;

namespace Roastgate.Core.Security;

/// <summary>
/// The server-side half of a passkey ceremony: the options (with the challenge) that were sent to the browser. Single use, five minutes,
/// so a recorded answer cannot be replayed and an old challenge cannot be answered later.
/// </summary>
public sealed record PasskeyCeremony(
    PasskeyCeremonyKind Kind,
    string OptionsJson,
    string? Ticket,
    DateTimeOffset ExpiresAt);
