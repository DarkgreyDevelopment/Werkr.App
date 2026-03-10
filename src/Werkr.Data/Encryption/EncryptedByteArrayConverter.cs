using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Werkr.Data.Encryption;

/// <summary>
/// EF Core value converter that transparently encrypts a <see cref="byte"/>[] property
/// to a Base64 string before writing and decrypts on read.
/// Uses <see cref="FieldEncryptionProvider"/> (AES-256-GCM).
/// </summary>
public sealed class EncryptedByteArrayConverter : ValueConverter<byte[], string> {

    /// <summary>Creates a new converter backed by the specified encryption provider.</summary>
    /// <param name="provider">The AES-256-GCM encryption provider.</param>
    public EncryptedByteArrayConverter( FieldEncryptionProvider provider )
        : base(
            v => provider.EncryptBytes( v )!,
            v => provider.DecryptBytes( v )!
        ) { }
}
