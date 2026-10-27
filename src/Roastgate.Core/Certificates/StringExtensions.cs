namespace Roastgate.Core.Certificates;

internal static class StringExtensions
{
    public static string? NullIfEmpty(this string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
