using Roastgate.Core.Settings;

namespace Roastgate.Core.Setup;

public sealed record SetupRequest(string Token, string Username, string Password, AppSettings Settings);
