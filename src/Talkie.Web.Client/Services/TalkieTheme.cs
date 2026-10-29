using MudBlazor;

namespace Talkie.Web.Client.Services;

/// <summary>The pastel blue Talkie look.</summary>
public static class TalkieTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#5B9BEA",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#9CC9F5",
            Tertiary = "#A8DCD1",
            Info = "#7EB6F0",
            Success = "#7CC8A4",
            Warning = "#F2C46D",
            Error = "#EE8C8C",
            Background = "#F4F8FE",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#2C3E5A",
            DrawerBackground = "#EAF2FD",
            DrawerText = "#2C3E5A",
            DrawerIcon = "#5B9BEA",
            TextPrimary = "#2C3E5A",
            TextSecondary = "#6B7D99",
            LinesDefault = "#DCE7F7",
            Divider = "#DCE7F7",
            TableLines = "#E5EEFA",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = ["Nunito", "Segoe UI", "Roboto", "sans-serif"] },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "14px",
            DrawerWidthLeft = "232px",
        },
    };

    /// <summary>Pastel colors offered when creating roles.</summary>
    public static readonly string[] RoleColors =
    [
        "#90CAF9", "#81D4FA", "#80DEEA", "#80CBC4", "#A5D6A7", "#C5E1A5",
        "#FFF59D", "#FFCC80", "#FFAB91", "#EF9A9A", "#F48FB1", "#CE93D8",
        "#B39DDB", "#9FA8DA", "#BCAAA4", "#B0BEC5",
    ];
}
