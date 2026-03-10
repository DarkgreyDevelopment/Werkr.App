namespace Werkr.Data.Encryption;

/// <summary>
/// EF Core value converter that transparently encrypts a <see cref="string"/> property
/// before writing to the database and decrypts it after reading.
/// Uses <see cref="FieldEncryptionProvider"/> (AES-256-GCM).
/// </summary>
public sealed class EncryptedStringConverter : ValueConverter<string, string> {

    /// <summary>Creates a new converter backed by the specified encryption provider.</summary>
    /// <param name="provider">The AES-256-GCM encryption provider.</param>
    public EncryptedStringConverter( FieldEncryptionProvider provider )
        : base(
            v => provider.Encrypt( v )!,
            v => provider.Decrypt( v )!
        ) { }
}
