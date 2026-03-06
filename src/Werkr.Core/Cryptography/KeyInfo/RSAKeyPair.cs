using System.Security.Cryptography;

namespace Werkr.Core.Cryptography.KeyInfo;

/// <summary>
/// Holds an RSA key pair (public and private parameters) with key size metadata.
/// </summary>
public class RSAKeyPair {
    /// <summary>The public key parameters.</summary>
    public RSAParameters PublicKey { get; set; }

    /// <summary>The full (private) key parameters.</summary>
    public RSAParameters PrivateKey { get; set; }

    /// <summary>Key size in bits.</summary>
    public int KeySize { get; set; } = 4096;

    /// <summary>
    /// Creates an empty RSA key pair. Use <see cref="EncryptionProvider.GenerateRSAKeyPair"/>
    /// to generate a populated key pair.
    /// </summary>
    public RSAKeyPair( ) { }

    /// <summary>
    /// Creates an RSA key pair with the specified parameters.
    /// </summary>
    /// <param name="publicKey">The RSA public key parameters.</param>
    /// <param name="privateKey">The RSA private key parameters (includes private components).</param>
    /// <param name="keySize">The key size in bits. Must be at least 2048 and divisible by 8.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when key size is invalid.</exception>
    public RSAKeyPair(
        RSAParameters publicKey,
        RSAParameters privateKey,
        int keySize = 4096
    ) {
        if (keySize < 2048) {
            throw new ArgumentOutOfRangeException(
                nameof( keySize ),
                keySize,
                "RSA key size must be at least 2048 bits."
            );
        }

        if (keySize % 8 != 0) {
            throw new ArgumentOutOfRangeException(
                nameof( keySize ),
                keySize,
                "RSA key size must be divisible by 8."
            );
        }

        PublicKey = publicKey;
        PrivateKey = privateKey;
        KeySize = keySize;
    }

    /// <summary>
    /// Gets the public key serialized as UTF-8 JSON bytes (Modulus + Exponent).
    /// </summary>
    /// <returns>UTF-8-encoded JSON bytes.</returns>
    public byte[] GetPublicKeyBytes( ) {
        return EncryptionProvider.SerializePublicKey( PublicKey );
    }

    /// <summary>
    /// Deserializes a public RSA key from UTF-8 JSON bytes.
    /// </summary>
    /// <param name="data">UTF-8-encoded JSON bytes containing Modulus and Exponent.</param>
    /// <returns>The deserialized RSA public key parameters.</returns>
    public static RSAParameters FromPublicKeyBytes( byte[] data ) {
        return EncryptionProvider.DeserializePublicKey( data );
    }
}
