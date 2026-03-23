using System.Text.Json;

namespace Werkr.Core.Credentials;

/// <summary>
/// Static helper for scanning task ActionParameters JSON for credential name references.
/// </summary>
public static class CredentialResolver {

    /// <summary>Known JSON property names that hold credential references.</summary>
    private static readonly string[] s_credentialPropertyNames = ["CredentialName", "AuthCredential"];

    /// <summary>
    /// Scans a JSON string for credential name references.
    /// </summary>
    /// <param name="actionParametersJson">The task ActionParameters JSON.</param>
    /// <returns>Distinct credential names found.</returns>
    public static IReadOnlyList<string> FindCredentialReferences( string? actionParametersJson ) {
        if (string.IsNullOrWhiteSpace( actionParametersJson )) {
            return [];
        }

        try {
            using JsonDocument doc = JsonDocument.Parse( actionParametersJson );
            HashSet<string> found = new( StringComparer.OrdinalIgnoreCase );
            ScanElement( doc.RootElement, found );
            return [.. found];
        } catch (JsonException) {
            return [];
        }
    }

    /// <summary>
    /// Replaces all occurrences of a credential name in ActionParameters JSON.
    /// Returns the updated JSON string, or null if no changes were made.
    /// </summary>
    public static string? ReplaceCredentialName( string? actionParametersJson, string oldName, string newName ) {
        if (string.IsNullOrWhiteSpace( actionParametersJson )) {
            return null;
        }

        try {
            using JsonDocument doc = JsonDocument.Parse( actionParametersJson );
            using System.IO.MemoryStream ms = new( );
            using (Utf8JsonWriter writer = new( ms )) {
                bool changed = WriteWithReplacement( doc.RootElement, writer, oldName, newName );
                if (!changed) {
                    return null;
                }
            }
            return System.Text.Encoding.UTF8.GetString( ms.ToArray( ) );
        } catch (JsonException) {
            return null;
        }
    }

    private static void ScanElement( JsonElement element, HashSet<string> found ) {
        switch (element.ValueKind) {
            case JsonValueKind.Object:
                foreach (JsonProperty prop in element.EnumerateObject( )) {
                    if (IsCredentialProperty( prop.Name ) && prop.Value.ValueKind == JsonValueKind.String) {
                        string? value = prop.Value.GetString( );
                        if (!string.IsNullOrEmpty( value )) {
                            _ = found.Add( value );
                        }
                    } else {
                        ScanElement( prop.Value, found );
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray( )) {
                    ScanElement( item, found );
                }
                break;
        }
    }

    private static bool WriteWithReplacement(
        JsonElement element, Utf8JsonWriter writer, string oldName, string newName
    ) {
        bool changed = false;

        switch (element.ValueKind) {
            case JsonValueKind.Object:
                writer.WriteStartObject( );
                foreach (JsonProperty prop in element.EnumerateObject( )) {
                    writer.WritePropertyName( prop.Name );
                    if (IsCredentialProperty( prop.Name )
                        && prop.Value.ValueKind == JsonValueKind.String
                        && string.Equals( prop.Value.GetString( ), oldName, StringComparison.OrdinalIgnoreCase )) {
                        writer.WriteStringValue( newName );
                        changed = true;
                    } else {
                        changed |= WriteWithReplacement( prop.Value, writer, oldName, newName );
                    }
                }
                writer.WriteEndObject( );
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray( );
                foreach (JsonElement item in element.EnumerateArray( )) {
                    changed |= WriteWithReplacement( item, writer, oldName, newName );
                }
                writer.WriteEndArray( );
                break;

            default:
                element.WriteTo( writer );
                break;
        }

        return changed;
    }

    private static bool IsCredentialProperty( string propertyName ) =>
        Array.Exists( s_credentialPropertyNames,
            name => string.Equals( name, propertyName, StringComparison.OrdinalIgnoreCase ) );
}
