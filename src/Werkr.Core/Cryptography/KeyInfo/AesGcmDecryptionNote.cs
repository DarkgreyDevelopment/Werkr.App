namespace Werkr.Core.Cryptography.KeyInfo;

/// <summary>
/// Holds nonce, tag, and order information for a single AES-GCM encrypted chunk.
/// </summary>
public class AesGcmDecryptionNote {
    /// <summary>The 12-byte nonce used for this chunk.</summary>
    public byte[] Nonce { get; set; } = new byte[EncryptionProvider.AesGcmNonceSize];

    /// <summary>The 16-byte authentication tag for this chunk.</summary>
    public byte[] Tag { get; set; } = new byte[EncryptionProvider.AesGcmTagSize];

    /// <summary>The order of this chunk in the encrypted data sequence.</summary>
    public int Order { get; set; }

    /// <summary>Creates a new empty <see cref="AesGcmDecryptionNote"/>.</summary>
    public AesGcmDecryptionNote( ) { }

    /// <summary>Creates a new <see cref="AesGcmDecryptionNote"/> with validation.</summary>
    /// <param name="nonce">The 12-byte nonce.</param>
    /// <param name="tag">The 16-byte authentication tag.</param>
    /// <param name="order">The chunk order index.</param>
    /// <exception cref="ArgumentException">Thrown when nonce or tag lengths are invalid.</exception>
    public AesGcmDecryptionNote(
        byte[] nonce,
        byte[] tag,
        int order
    ) {
        if (nonce.Length != EncryptionProvider.AesGcmNonceSize) {
            throw new ArgumentException(
                $"Nonce must be {EncryptionProvider.AesGcmNonceSize} bytes, got {nonce.Length}.",
                nameof( nonce )
            );
        }

        if (tag.Length != EncryptionProvider.AesGcmTagSize) {
            throw new ArgumentException(
                $"Tag must be {EncryptionProvider.AesGcmTagSize} bytes, got {tag.Length}.",
                nameof( tag )
            );
        }

        Nonce = nonce;
        Tag = tag;
        Order = order;
    }
}
