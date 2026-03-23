using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Werkr.Data.Encryption;

/// <summary>
/// EF Core value converter that transparently encrypts a nullable <see cref="byte"/>[]
/// property to a Base64 string before writing and decrypts on read.
/// Null values pass through without encryption.
/// Uses <see cref="FieldEncryptionProvider"/> (AES-256-GCM).
/// </summary>
/// <remarks>Creates a new converter backed by the specified encryption provider.</remarks>
/// <param name="provider">The AES-256-GCM encryption provider.</param>
public sealed class EncryptedNullableByteArrayConverter( FieldEncryptionProvider provider ) : ValueConverter<byte[]?, string?>(
        v => v == null ? null : provider.EncryptBytes( v ),
        v => v == null ? null : provider.DecryptBytes( v )
        ) {
}
