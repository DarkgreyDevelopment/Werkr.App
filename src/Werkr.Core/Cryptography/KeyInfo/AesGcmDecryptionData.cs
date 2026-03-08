namespace Werkr.Core.Cryptography.KeyInfo;

/// <summary>
/// Holds AES-GCM decryption data: the symmetric key and ordered chunk notes.
/// </summary>
public class AesGcmDecryptionData {
    /// <summary>The 32-byte AES-256-GCM symmetric key.</summary>
    public byte[] Key { get; set; } = [];

    /// <summary>Ordered list of decryption notes (one per encrypted chunk).</summary>
    public List<AesGcmDecryptionNote> Notes { get; set; } = [];

    /// <summary>Creates a new empty <see cref="AesGcmDecryptionData"/>.</summary>
    public AesGcmDecryptionData( ) { }

    /// <summary>Creates a new <see cref="AesGcmDecryptionData"/> with validation.</summary>
    /// <param name="key">The 32-byte AES-256 key.</param>
    /// <param name="notes">The ordered decryption notes.</param>
    /// <exception cref="ArgumentException">Thrown when key length is not 32 bytes.</exception>
    public AesGcmDecryptionData(
        byte[] key,
        List<AesGcmDecryptionNote> notes
    ) {
        if (key.Length != EncryptionProvider.AesGcmKeySize) {
            throw new ArgumentException(
                $"AES-GCM key must be {EncryptionProvider.AesGcmKeySize} bytes, got {key.Length}.",
                nameof( key )
            );
        }

        Key = key;
        Notes = notes;
    }
}
