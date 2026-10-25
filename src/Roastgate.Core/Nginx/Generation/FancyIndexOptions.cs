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

/// <summary>
/// The fancyindex directory listing of a static site (see <see cref="SiteSpec.FancyIndex"/>). Each property maps to one
/// <c>fancyindex_*</c> directive and defaults to the module's own default.
/// </summary>
public sealed record FancyIndexOptions
{
    /// <summary>How entries are ordered when the visitor has not chosen (<c>fancyindex_default_sort</c>).</summary>
    public FancyIndexSort DefaultSort { get; init; } = FancyIndexSort.Name;

    /// <summary>Sort names with case taken into account (<c>fancyindex_case_sensitive</c>).</summary>
    public bool CaseSensitive { get; init; } = true;

    /// <summary>List directories before files (<c>fancyindex_directories_first</c>).</summary>
    public bool DirectoriesFirst { get; init; } = true;

    /// <summary>
    /// URL of a style sheet linked after the built-in rules, so it can override them (<c>fancyindex_css_href</c>).
    /// </summary>
    public string? CssHref { get; init; } = null;

    /// <summary>Show sizes in bytes instead of a rounded value (<c>fancyindex_exact_size</c>).</summary>
    public bool ExactSize { get; init; } = true;

    /// <summary>
    /// URL path of an HTML file shown below the listing (<c>fancyindex_footer</c>). Null keeps the module's own footer.
    /// </summary>
    public string? Footer { get; init; }

    /// <summary>
    /// URL path of an HTML file shown above the listing (<c>fancyindex_header</c>). Null keeps the module's own header.
    /// </summary>
    public string? Header { get; init; }

    /// <summary>Show the current path above the listing (<c>fancyindex_show_path</c>).</summary>
    public bool ShowPath { get; init; } = true;

    /// <summary>List files whose name starts with a dot (<c>fancyindex_show_dotfiles</c>).</summary>
    public bool ShowDotFiles { get; init; } = false;

    /// <summary>File names left out of the listing (<c>fancyindex_ignore</c>).</summary>
    public IReadOnlyList<string> Ignore { get; init; } = [];

    /// <summary>Leave symbolic links out of the listing (<c>fancyindex_hide_symlinks</c>).</summary>
    public bool HideSymlinks { get; init; } = false;

    /// <summary>Leave the parent directory link out of the listing (<c>fancyindex_hide_parent_dir</c>).</summary>
    public bool HideParentDir { get; init; } = false;

    /// <summary>Show times in the server's time zone instead of UTC (<c>fancyindex_localtime</c>).</summary>
    public bool LocalTime { get; init; } = false;

    /// <summary>strftime format of the modification time (<c>fancyindex_time_format</c>).</summary>
    public string TimeFormat { get; init; } = "%Y-%b-%d %H:%M";
}
