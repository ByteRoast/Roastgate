namespace Roastgate.Core.Data.Models;

/// <summary>A key/value setting (the structured application settings are stored as one JSON document under the key "app").</summary>
public sealed class Setting
{
    public required string Key { get; set; }

    public required string Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
