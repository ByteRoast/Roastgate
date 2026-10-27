namespace Roastgate.Core.Nginx.Generation;

public enum MetadataStatus
{
    /// <summary>The file was not produced by the wizard.</summary>
    None,

    /// <summary>Produced by the wizard and untouched since.</summary>
    Intact,

    /// <summary>Produced by the wizard, but the content below the header was changed by hand.</summary>
    EditedByHand,

    /// <summary>
    /// It carries the wizard header, but the stored settings cannot be read (corrupted or from a newer version).
    /// </summary>
    Unreadable,
}
