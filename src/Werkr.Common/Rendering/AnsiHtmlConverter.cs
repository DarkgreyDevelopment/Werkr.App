using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Werkr.Common.Rendering;

/// <summary>
/// Converts ANSI SGR escape sequences to safe HTML <c>&lt;span&gt;</c> elements.
/// Handles the standard 16-color palette (foreground and background), bold, and reset.
/// Unsupported sequences are stripped. All text content is HTML-encoded to prevent XSS.
/// </summary>
public static partial class AnsiHtmlConverter {
    [GeneratedRegex( @"\x1b\[([0-9;]*)m" )]
    private static partial Regex SgrPattern( );

    private static readonly Dictionary<int, string> s_fgColors = new( ) {
        [30] = "#000",      // Black
        [31] = "#c00",      // Red
        [32] = "#0a0",      // Green
        [33] = "#a50",      // Yellow/DarkYellow
        [34] = "#00a",      // Blue/DarkBlue
        [35] = "#a0a",      // Magenta/DarkMagenta
        [36] = "#0aa",      // Cyan/DarkCyan
        [37] = "#ccc",      // White/Gray
        [90] = "#666",      // DarkGray (bright black)
        [91] = "#f55",      // Bright Red
        [92] = "#5f5",      // Bright Green
        [93] = "#ff5",      // Bright Yellow
        [94] = "#55f",      // Bright Blue
        [95] = "#f5f",      // Bright Magenta
        [96] = "#5ff",      // Bright Cyan
        [97] = "#fff",      // Bright White
    };

    private static readonly Dictionary<int, string> s_bgColors = new( ) {
        [40] = "#000",
        [41] = "#c00",
        [42] = "#0a0",
        [43] = "#a50",
        [44] = "#00a",
        [45] = "#a0a",
        [46] = "#0aa",
        [47] = "#ccc",
        [100] = "#666",
        [101] = "#f55",
        [102] = "#5f5",
        [103] = "#ff5",
        [104] = "#55f",
        [105] = "#f5f",
        [106] = "#5ff",
        [107] = "#fff",
    };

    /// <summary>
    /// Converts a string containing ANSI SGR escape sequences into HTML
    /// with inline <c>style</c> attributes for the standard 16-color palette,
    /// bold, and reset.
    /// </summary>
    /// <param name="input">The ANSI-encoded string to convert.</param>
    /// <returns>
    /// HTML string with <c>&lt;span&gt;</c> elements for styled regions,
    /// or an empty string if <paramref name="input"/> is null/empty.
    /// </returns>
    public static string Convert( string input ) {
        if (string.IsNullOrEmpty( input )) {
            return string.Empty;
        }

        // Fast path: no escape sequences at all
        if (!input.Contains( '\x1b' )) {
            return WebUtility.HtmlEncode( input );
        }

        StringBuilder sb = new( input.Length * 2 );
        string? currentFg = null;
        string? currentBg = null;
        bool currentBold = false;
        bool spanOpen = false;
        int lastIndex = 0;

        foreach (Match match in SgrPattern( ).Matches( input )) {
            // Emit text before this escape sequence (HTML-encoded)
            if (match.Index > lastIndex) {
                string text = input[lastIndex..match.Index];
                if (!spanOpen && (currentFg is not null || currentBg is not null || currentBold)) {
                    _ = sb.Append( BuildSpanOpen(
                        currentFg,
                        currentBg,
                        currentBold
                    ) );
                    spanOpen = true;
                }
                _ = sb.Append( WebUtility.HtmlEncode( text ) );
            }

            lastIndex = match.Index + match.Length;

            // Parse SGR parameters
            string paramStr = match.Groups[1].Value;
            int[] codes = string.IsNullOrEmpty( paramStr )
                ? [0]
                : [.. paramStr.Split( ';' ).Select(
                    s => int.TryParse(
                        s,
                        out int v
                    ) ? v : 0
                )];

            foreach (int code in codes) {
                if (code == 0) {
                    // Reset all
                    if (spanOpen) {
                        _ = sb.Append( "</span>" );
                        spanOpen = false;
                    }
                    currentFg = null;
                    currentBg = null;
                    currentBold = false;
                } else if (code == 1) {
                    if (spanOpen) { _ = sb.Append( "</span>" ); spanOpen = false; }
                    currentBold = true;
                } else if (code == 22) {
                    if (spanOpen) { _ = sb.Append( "</span>" ); spanOpen = false; }
                    currentBold = false;
                } else if (s_fgColors.TryGetValue(
                    code,
                    out string? fg
                )) {
                    if (spanOpen) { _ = sb.Append( "</span>" ); spanOpen = false; }
                    currentFg = fg;
                } else if (s_bgColors.TryGetValue(
                    code,
                    out string? bg
                )) {
                    if (spanOpen) { _ = sb.Append( "</span>" ); spanOpen = false; }
                    currentBg = bg;
                } else if (code == 39) {
                    // Default foreground
                    if (spanOpen) { _ = sb.Append( "</span>" ); spanOpen = false; }
                    currentFg = null;
                } else if (code == 49) {
                    // Default background
                    if (spanOpen) { _ = sb.Append( "</span>" ); spanOpen = false; }
                    currentBg = null;
                }
                // All other codes are silently ignored (stripped)
            }
        }

        // Emit remaining text
        if (lastIndex < input.Length) {
            string remaining = input[lastIndex..];
            if (!spanOpen && (currentFg is not null || currentBg is not null || currentBold)) {
                _ = sb.Append( BuildSpanOpen(
                    currentFg,
                    currentBg,
                    currentBold
                ) );
                spanOpen = true;
            }
            _ = sb.Append( WebUtility.HtmlEncode( remaining ) );
        }

        if (spanOpen) {
            _ = sb.Append( "</span>" );
        }

        return sb.ToString( );
    }

    /// <summary>
    /// Removes all ANSI SGR escape sequences from the input, returning plain text.
    /// </summary>
    /// <param name="input">The string to strip.</param>
    /// <returns>The input with all ANSI escape sequences removed.</returns>
    public static string Strip( string input ) {
        return string.IsNullOrEmpty( input ) || !input.Contains( '\x1b' )
            ? input
            : SgrPattern( ).Replace(
            input,
            string.Empty
        );
    }

    /// <summary>
    /// Returns the ANSI SGR foreground escape sequence for the specified <see cref="ConsoleColor"/>.
    /// </summary>
    /// <param name="color">The console color to convert.</param>
    /// <returns>The ANSI escape string (e.g. <c>\x1b[31m</c>), or empty for unsupported values.</returns>
    public static string ConsoleColorToAnsi( ConsoleColor color ) =>
        color switch {
            ConsoleColor.Black => "\x1b[30m",
            ConsoleColor.DarkRed => "\x1b[31m",
            ConsoleColor.DarkGreen => "\x1b[32m",
            ConsoleColor.DarkYellow => "\x1b[33m",
            ConsoleColor.DarkBlue => "\x1b[34m",
            ConsoleColor.DarkMagenta => "\x1b[35m",
            ConsoleColor.DarkCyan => "\x1b[36m",
            ConsoleColor.Gray => "\x1b[37m",
            ConsoleColor.DarkGray => "\x1b[90m",
            ConsoleColor.Red => "\x1b[91m",
            ConsoleColor.Green => "\x1b[92m",
            ConsoleColor.Yellow => "\x1b[93m",
            ConsoleColor.Blue => "\x1b[94m",
            ConsoleColor.Magenta => "\x1b[95m",
            ConsoleColor.Cyan => "\x1b[96m",
            ConsoleColor.White => "\x1b[97m",
            _ => string.Empty,
        };

    /// <summary>
    /// Returns the ANSI SGR background escape sequence for the specified <see cref="ConsoleColor"/>.
    /// </summary>
    /// <param name="color">The console color to convert.</param>
    /// <returns>The ANSI background escape string (e.g. <c>\x1b[41m</c>), or empty for unsupported values.</returns>
    public static string ConsoleColorToBgAnsi( ConsoleColor color ) =>
        color switch {
            ConsoleColor.Black => "\x1b[40m",
            ConsoleColor.DarkRed => "\x1b[41m",
            ConsoleColor.DarkGreen => "\x1b[42m",
            ConsoleColor.DarkYellow => "\x1b[43m",
            ConsoleColor.DarkBlue => "\x1b[44m",
            ConsoleColor.DarkMagenta => "\x1b[45m",
            ConsoleColor.DarkCyan => "\x1b[46m",
            ConsoleColor.Gray => "\x1b[47m",
            ConsoleColor.DarkGray => "\x1b[100m",
            ConsoleColor.Red => "\x1b[101m",
            ConsoleColor.Green => "\x1b[102m",
            ConsoleColor.Yellow => "\x1b[103m",
            ConsoleColor.Blue => "\x1b[104m",
            ConsoleColor.Magenta => "\x1b[105m",
            ConsoleColor.Cyan => "\x1b[106m",
            ConsoleColor.White => "\x1b[107m",
            _ => string.Empty,
        };

    /// <summary>The ANSI SGR reset sequence (<c>\x1b[0m</c>) that clears all formatting.</summary>
    public const string AnsiReset = "\x1b[0m";

    private static string BuildSpanOpen(
        string? fg,
        string? bg,
        bool bold
    ) {
        StringBuilder style = new( );
        if (fg is not null) {
            _ = style.Append( $"color:{fg};" );
        }

        if (bg is not null) {
            _ = style.Append( $"background:{bg};" );
        }

        if (bold) {
            _ = style.Append( "font-weight:bold;" );
        }

        return $"<span style=\"{style}\">";
    }
}
