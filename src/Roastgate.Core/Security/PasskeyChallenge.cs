namespace Roastgate.Core.Security;

/// <summary>What the browser needs to start a ceremony: the options for <c>navigator.credentials</c>, and the id of the server-side state.</summary>
public sealed record PasskeyChallenge(string CeremonyId, string OptionsJson);
