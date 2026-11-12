using System.ComponentModel;

namespace Roastgate.Core.Nginx.Generation;

/// <summary>How fancyindex orders the entries of a listing.</summary>
public enum FancyIndexSort
{
    /// <summary>By name, A to Z.</summary>
    [Description("name")] Name,

    /// <summary>By name, Z to A.</summary>
    [Description("name_desc")] NameDescending,

    /// <summary>By size, smallest first.</summary>
    [Description("size")] Size,

    /// <summary>By size, largest first.</summary>
    [Description("size_desc")] SizeDescending,

    /// <summary>By modification date, oldest first.</summary>
    [Description("date")] Date,

    /// <summary>By modification date, newest first.</summary>
    [Description("date_desc")] DateDescending,
}
