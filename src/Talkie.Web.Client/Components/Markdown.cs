using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Talkie.Web.Client.Components;

/// <summary>A tiny, safe markdown renderer (bold, italic, inline code and bullet lists) for assistant replies.</summary>
public static partial class Markdown
{
    public static MarkupString Render(string text)
    {
        var sb = new StringBuilder();
        var inList = false;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = Inline(WebUtility.HtmlEncode(raw));
            if (raw.TrimStart().StartsWith("- ") || raw.TrimStart().StartsWith("* "))
            {
                if (!inList) { sb.Append("<ul>"); inList = true; }
                sb.Append("<li>").Append(Inline(WebUtility.HtmlEncode(raw.TrimStart()[2..]))).Append("</li>");
                continue;
            }
            if (inList) { sb.Append("</ul>"); inList = false; }
            if (raw.Trim().Length > 0) sb.Append("<p>").Append(line).Append("</p>");
        }
        if (inList) sb.Append("</ul>");
        return new MarkupString(sb.ToString());
    }

    private static string Inline(string html)
    {
        html = Bold().Replace(html, "<strong>$1</strong>");
        html = Italic().Replace(html, "<em>$1</em>");
        html = Code().Replace(html, "<code>$1</code>");
        return html;
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex Bold();
    [GeneratedRegex(@"(?<!\*)\*(?!\s)(.+?)(?<!\s)\*(?!\*)")] private static partial Regex Italic();
    [GeneratedRegex(@"`(.+?)`")] private static partial Regex Code();
}
