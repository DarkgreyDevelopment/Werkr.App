using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Werkr.Data.Encryption;

/// <summary>
/// EF Core value converter that serializes <see cref="RSAParameters"/> to JSON
/// and then encrypts with AES-256-GCM before writing to the database.
/// Decrypts and deserializes on read. Overrides the global
/// <c>RSAParametersStringConverter</c> convention for specifically targeted properties.
/// </summary>
/// <param name="provider">The AES-256-GCM encryption provider.</param>
public sealed class EncryptedRSAParametersConverter( FieldEncryptionProvider provider ) : ValueConverter<RSAParameters, string>(
    v => provider.Encrypt( JsonSerializer.Serialize( v ) )!,
    v => JsonSerializer.Deserialize<RSAParameters>( provider.Decrypt( v )! )
) {
}
