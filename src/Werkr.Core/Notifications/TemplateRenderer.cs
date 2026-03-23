using System.Text.RegularExpressions;

namespace Werkr.Core.Notifications;

/// <summary>
/// Simple <c>{{variableName}}</c> template renderer for notification messages.
/// Undefined variables render as empty string (not error).
/// </summary>
public static partial class TemplateRenderer {

    /// <summary>
    /// Renders a template by replacing <c>{{variableName}}</c> placeholders
    /// with values from the provided dictionary.
    /// </summary>
    /// <param name="template">The template string with <c>{{variable}}</c> placeholders.</param>
    /// <param name="variables">Variable name-value pairs for substitution.</param>
    /// <returns>The rendered string with all placeholders replaced.</returns>
    public static string Render( string template, IReadOnlyDictionary<string, string> variables ) {
        return string.IsNullOrEmpty( template )
            ? string.Empty
            : TemplatePattern( ).Replace( template, match => {
                string variableName = match.Groups[1].Value.Trim( );
                return variables.TryGetValue( variableName, out string? value ) ? value : string.Empty;
            } );
    }

    [GeneratedRegex( @"\{\{(\s*[\w.]+\s*)\}\}" )]
    private static partial Regex TemplatePattern( );
}
