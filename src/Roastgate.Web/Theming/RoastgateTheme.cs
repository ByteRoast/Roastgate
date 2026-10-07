using MudBlazor;

namespace Roastgate.Web.Theming;

/// <summary>Light theme taken from the byteroast.fr (Docusaurus) palette.</summary>
public static class RoastgateTheme
{
    public const string Primary = "#ee8a26";
    public const string PrimaryDarkest = "#9f560d";
    public const string Ink = "#1c1e21";
    public const string InkSecondary = "#525860";
    public const string Border = "#dadde1";
    public const string Footer = "#303846";

    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Primary,
            // White on #ee8a26 is only ~2.5:1, so primary buttons carry dark text instead.
            PrimaryContrastText = Ink,
            Secondary = Footer,
            SecondaryContrastText = "#ffffff",
            Tertiary = PrimaryDarkest,
            TertiaryContrastText = "#ffffff",
            Info = "#54c7ec",
            InfoContrastText = Ink,
            Success = "#00a400",
            SuccessContrastText = "#ffffff",
            Warning = "#ffba00",
            WarningContrastText = Ink,
            Error = "#fa383e",
            ErrorContrastText = "#ffffff",
            Background = "#ffffff",
            BackgroundGray = "#f5f6f7",
            Surface = "#ffffff",
            AppbarBackground = "#ffffff",
            AppbarText = Ink,
            DrawerBackground = "#ffffff",
            DrawerText = Ink,
            DrawerIcon = InkSecondary,
            TextPrimary = Ink,
            TextSecondary = InkSecondary,
            ActionDefault = InkSecondary,
            LinesDefault = Border,
            LinesInputs = Border,
            TableLines = Border,
            Divider = Border,
            DividerLight = "#ebedf0",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "0.4rem",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily =
                [
                    "system-ui", "-apple-system", "Segoe UI", "Roboto", "Ubuntu", "Cantarell", "Noto Sans", "sans-serif",
                ],
            },
        },
    };
}
