using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Werkr.Data.Encryption;

/// <summary>
/// EF Core value converter that transparently encrypts a <see cref="string"/> property
/// before writing to the database and decrypts it after reading.
/// Uses <see cref="FieldEncryptionProvider"/> (AES-256-GCM).
/// </summary>
/// <remarks>Creates a new converter backed by the specified encryption provider.</remarks>
/// <param name="provider">The AES-256-GCM encryption provider.</param>
public sealed class EncryptedStringConverter( FieldEncryptionProvider provider ) : ValueConverter<string, string>(
        v => provider.Encrypt( v )!,
        v => provider.Decrypt( v )!
        ) {
}
