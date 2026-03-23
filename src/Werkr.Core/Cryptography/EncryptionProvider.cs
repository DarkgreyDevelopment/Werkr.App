using System.Security.Cryptography;
using System.Text.Json;

namespace Werkr.Core.Cryptography;

/// <summary>
/// Provides RSA-4096, AES-256-GCM, and hybrid cryptographic operations.
/// <para>
/// All RSA operations use <c>OaepSHA512</c> padding — agent registration key exchange and periodic shared key rotation.
/// SHA-512 is the default for all hashing and RSA OAEP operations.
/// </para>
/// </summary>
public static class EncryptionProvider {
    /// <summary>AES-GCM key size in bytes (256 bits).</summary>
    public const int AesGcmKeySize = 32;

    /// <summary>AES-GCM nonce size in bytes (96 bits).</summary>
    public const int AesGcmNonceSize = 12;

    /// <summary>AES-GCM authentication tag size in bytes (128 bits).</summary>
    public const int AesGcmTagSize = 16;

    /// <summary>RSA-4096 encrypted output size in bytes.</summary>
    public const int RsaEncryptedBlockSize = 512;

    // --- RSA Operations ---

    /// <summary>Generates a new RSA key pair of the specified size.</summary>
    /// <param name="keySize">Key size in bits (minimum 2048, must be divisible by 8). Default is 4096.</param>
    /// <returns>An <see cref="KeyInfo.RSAKeyPair"/> containing public and private parameters.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when key size is less than 2048 or not divisible by
    /// 8.</exception>
    public static KeyInfo.RSAKeyPair GenerateRSAKeyPair( int keySize = 4096 ) {
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

        using RSA rsa = RSA.Create( keySize );
        RSAParameters publicKey = rsa.ExportParameters( includePrivateParameters: false );
        RSAParameters privateKey = rsa.ExportParameters( includePrivateParameters: true );
        return new KeyInfo.RSAKeyPair(
            publicKey,
            privateKey,
            keySize
        );
    }

    /// <summary>Encrypts data using RSA OAEP with SHA-512 padding.</summary>
    /// <param name="data">The plaintext data to encrypt. Must be within RSA OAEP payload limit.</param>
    /// <param name="publicKey">The recipient's RSA public key.</param>
    /// <returns>The RSA-encrypted ciphertext.</returns>
    /// <exception cref="WerkrCryptoException">Thrown when encryption fails.</exception>
    public static byte[] RSAEncrypt(
        byte[] data,
        RSAParameters publicKey
    ) {
        try {
            using RSA rsa = RSA.Create( );
            rsa.ImportParameters( publicKey );
            return rsa.Encrypt(
                data,
                RSAEncryptionPadding.OaepSHA512
            );
        } catch (CryptographicException ex) {
            throw new WerkrCryptoException(
                "RSA encryption failed — data may exceed OAEP payload limit or key is invalid.",
                ex
            );
        }
    }

    /// <summary>Decrypts data using RSA OAEP with SHA-512 padding.</summary>
    /// <param name="data">The ciphertext to decrypt.</param>
    /// <param name="privateKey">The recipient's RSA private key.</param>
    /// <returns>The decrypted plaintext.</returns>
    /// <exception cref="WerkrCryptoException">Thrown when decryption fails (wrong key or corrupted data).</exception>
    public static byte[] RSADecrypt(
        byte[] data,
        RSAParameters privateKey
    ) {
        try {
            using RSA rsa = RSA.Create( );
            rsa.ImportParameters( privateKey );
            return rsa.Decrypt(
                data,
                RSAEncryptionPadding.OaepSHA512
            );
        } catch (CryptographicException ex) {
            throw new WerkrCryptoException(
                "RSA decryption failed — wrong key or corrupted data.",
                ex
            );
        }
    }

    /// <summary>Signs data with an RSA private key using SHA-512.</summary>
    /// <param name="data">The data to sign.</param>
    /// <param name="privateKey">The signer's RSA private key.</param>
    /// <returns>The RSA signature bytes.</returns>
    /// <exception cref="WerkrCryptoException">Thrown when signing fails.</exception>
    public static byte[] Sign(
        byte[] data,
        RSAParameters privateKey
    ) {
        try {
            using RSA rsa = RSA.Create( );
            rsa.ImportParameters( privateKey );
            return rsa.SignData(
                data,
                HashAlgorithmName.SHA512,
                RSASignaturePadding.Pkcs1
            );
        } catch (CryptographicException ex) {
            throw new WerkrCryptoException(
                "RSA signing failed.",
                ex
            );
        }
    }

    /// <summary>Verifies an RSA signature using SHA-512.</summary>
    /// <param name="data">The original data that was signed.</param>
    /// <param name="signature">The signature to verify.</param>
    /// <param name="publicKey">The signer's RSA public key.</param>
    /// <returns><c>true</c> if the signature is valid; otherwise <c>false</c>.</returns>
    public static bool Verify(
        byte[] data,
        byte[] signature,
        RSAParameters publicKey
    ) {
        try {
            using RSA rsa = RSA.Create( );
            rsa.ImportParameters( publicKey );
            return rsa.VerifyData(
                data,
                signature,
                HashAlgorithmName.SHA512,
                RSASignaturePadding.Pkcs1
            );
        } catch (CryptographicException) {
            return false;
        }
    }

    // --- AES-256-GCM Operations ---

    /// <summary>Encrypts data using AES-256-GCM with a random nonce.</summary>
    /// <param name="plaintext">The data to encrypt.</param>
    /// <param name="key">The 32-byte AES-256 key.</param>
    /// <param name="nonce">Output: the 12-byte random nonce used.</param>
    /// <param name="tag">Output: the 16-byte authentication tag.</param>
    /// <returns>The ciphertext bytes.</returns>
    /// <exception cref="WerkrCryptoException">Thrown when encryption fails.</exception>
    public static byte[] AesGcmEncrypt(
        byte[] plaintext,
        byte[] key,
        out byte[] nonce,
        out byte[] tag
    ) {
        if (key.Length != AesGcmKeySize) {
            throw new ArgumentException(
                $"AES-GCM key must be {AesGcmKeySize} bytes.",
                nameof( key )
            );
        }

        try {
            nonce = GenerateRandomBytes( AesGcmNonceSize );
            tag = new byte[AesGcmTagSize];
            byte[] ciphertext = new byte[plaintext.Length];

            using AesGcm aes = new(
                key,
                AesGcmTagSize
            );
            aes.Encrypt(
                nonce,
                plaintext,
                ciphertext,
                tag
            );
            return ciphertext;
        } catch (CryptographicException ex) {
            throw new WerkrCryptoException(
                "AES-GCM encryption failed.",
                ex
            );
        }
    }

    /// <summary>Decrypts data using AES-256-GCM.</summary>
    /// <param name="ciphertext">The ciphertext to decrypt.</param>
    /// <param name="key">The 32-byte AES-256 key.</param>
    /// <param name="nonce">The 12-byte nonce used during encryption.</param>
    /// <param name="tag">The 16-byte authentication tag.</param>
    /// <returns>The decrypted plaintext.</returns>
    /// <exception cref="WerkrCryptoException">Thrown when decryption fails (wrong key, tampered data, or tag
    /// mismatch).</exception>
    public static byte[] AesGcmDecrypt(
        byte[] ciphertext,
        byte[] key,
        byte[] nonce,
        byte[] tag
    ) {
        if (key.Length != AesGcmKeySize) {
            throw new ArgumentException(
                $"AES-GCM key must be {AesGcmKeySize} bytes.",
                nameof( key )
            );
        }

        try {
            byte[] plaintext = new byte[ciphertext.Length];
            using AesGcm aes = new(
                key,
                AesGcmTagSize
            );
            aes.Decrypt(
                nonce,
                ciphertext,
                tag,
                plaintext
            );
            return plaintext;
        } catch (CryptographicException ex) {
            throw new WerkrCryptoException(
                "AES-GCM authentication tag mismatch — data tampered or wrong key.",
                ex
            );
        }
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM with a password-derived key.
    /// The password is hashed with SHA-512 and truncated to 32 bytes.
    /// Output format: <c>nonce (12) ‖ tag (16) ‖ ciphertext</c>.
    /// </summary>
    /// <param name="plaintext">The data to encrypt.</param>
    /// <param name="password">The password used to derive the AES key.</param>
    /// <returns>Combined bytes: nonce + tag + ciphertext.</returns>
    /// <exception cref="WerkrCryptoException">Thrown when encryption fails.</exception>
    public static byte[] AesGcmPasswordEncrypt(
        byte[] plaintext,
        string password
    ) {
        byte[] fullHash = SHA512.HashData( System.Text.Encoding.UTF8.GetBytes( password ) );
        byte[] key = fullHash[..AesGcmKeySize];

        byte[] ciphertext = AesGcmEncrypt(
            plaintext,
            key,
            out byte[] nonce,
            out byte[] tag
        );

        // Output: nonce (12) ‖ tag (16) ‖ ciphertext
        byte[] result = new byte[AesGcmNonceSize + AesGcmTagSize + ciphertext.Length];
        Buffer.BlockCopy(
            nonce,
            0,
            result,
            0,
            AesGcmNonceSize
        );
        Buffer.BlockCopy(
            tag,
            0,
            result,
            AesGcmNonceSize,
            AesGcmTagSize
        );
        Buffer.BlockCopy(
            ciphertext,
            0,
            result,
            AesGcmNonceSize + AesGcmTagSize,
            ciphertext.Length
        );

        return result;
    }

    /// <summary>
    /// Decrypts data that was encrypted with <see cref="AesGcmPasswordEncrypt"/>.
    /// Input format: <c>nonce (12) ‖ tag (16) ‖ ciphertext</c>.
    /// </summary>
    /// <param name="encryptedData">Combined nonce + tag + ciphertext bytes.</param>
    /// <param name="password">The password used during encryption.</param>
    /// <returns>The decrypted plaintext.</returns>
    /// <exception cref="WerkrCryptoException">Thrown when decryption fails (wrong password or tampered
    /// data).</exception>
    public static byte[] AesGcmPasswordDecrypt(
        byte[] encryptedData,
        string password
    ) {
        if (encryptedData.Length < AesGcmNonceSize + AesGcmTagSize) {
            throw new WerkrCryptoException( "Encrypted data is too short — expected at least nonce + tag bytes." );
        }

        byte[] fullHash = SHA512.HashData( System.Text.Encoding.UTF8.GetBytes( password ) );
        byte[] key = fullHash[..AesGcmKeySize];

        byte[] nonce = encryptedData[..AesGcmNonceSize];
        byte[] tag = encryptedData[AesGcmNonceSize..( AesGcmNonceSize + AesGcmTagSize )];
        byte[] ciphertext = encryptedData[( AesGcmNonceSize + AesGcmTagSize )..];

        return AesGcmDecrypt(
            ciphertext,
            key,
            nonce,
            tag
        );
    }

    // --- Hybrid Encryption ---

    /// <summary>
    /// Hybrid-encrypts data: generates a random AES-256 key, AES-GCM-encrypts the data,
    /// then RSA-OAEP-SHA-512 encrypts only the AES key.
    /// Output format: <c>rsaEncryptedKey (512) ‖ nonce (12) ‖ tag (16) ‖ ciphertext</c>.
    /// </summary>
    /// <param name="data">The plaintext data to encrypt (no size limit).</param>
    /// <param name="recipientPublicKey">The recipient's RSA public key.</param>
    /// <returns>The hybrid-encrypted envelope bytes.</returns>
    /// <exception cref="WerkrCryptoException">Thrown when encryption fails.</exception>
    public static byte[] HybridEncrypt(
        byte[] data,
        RSAParameters recipientPublicKey
    ) {
        byte[] aesKey = GenerateRandomBytes( AesGcmKeySize );
        byte[] ciphertext = AesGcmEncrypt(
            data,
            aesKey,
            out byte[] nonce,
            out byte[] tag
        );
        byte[] rsaEncryptedKey = RSAEncrypt(
            aesKey,
            recipientPublicKey
        );

        // Output: rsaEncryptedKey (512 for RSA-4096) ‖ nonce (12) ‖ tag (16) ‖ ciphertext
        byte[] envelope = new byte[rsaEncryptedKey.Length + AesGcmNonceSize + AesGcmTagSize + ciphertext.Length];
        int offset = 0;
        Buffer.BlockCopy(
            rsaEncryptedKey,
            0,
            envelope,
            offset,
            rsaEncryptedKey.Length
        );
        offset += rsaEncryptedKey.Length;
        Buffer.BlockCopy(
            nonce,
            0,
            envelope,
            offset,
            AesGcmNonceSize
        );
        offset += AesGcmNonceSize;
        Buffer.BlockCopy(
            tag,
            0,
            envelope,
            offset,
            AesGcmTagSize
        );
        offset += AesGcmTagSize;
        Buffer.BlockCopy(
            ciphertext,
            0,
            envelope,
            offset,
            ciphertext.Length
        );

        return envelope;
    }

    /// <summary>
    /// Decrypts a hybrid-encrypted envelope produced by <see cref="HybridEncrypt"/>.
    /// Input format: <c>rsaEncryptedKey (512) ‖ nonce (12) ‖ tag (16) ‖ ciphertext</c>.
    /// </summary>
    /// <param name="envelope">The hybrid-encrypted envelope bytes.</param>
    /// <param name="recipientPrivateKey">The recipient's RSA private key.</param>
    /// <returns>The decrypted plaintext.</returns>
    /// <exception cref="WerkrCryptoException">Thrown when decryption fails.</exception>
    public static byte[] HybridDecrypt(
        byte[] envelope,
        RSAParameters recipientPrivateKey
    ) {
        int minimumLength = RsaEncryptedBlockSize + AesGcmNonceSize + AesGcmTagSize;
        if (envelope.Length < minimumLength) {
            throw new WerkrCryptoException(
                "Hybrid envelope too short — expected at least " +
                $"{minimumLength} bytes, got {envelope.Length}." );
        }

        byte[] rsaEncryptedKey = envelope[..RsaEncryptedBlockSize];
        byte[] nonce = envelope[RsaEncryptedBlockSize..( RsaEncryptedBlockSize + AesGcmNonceSize )];
        int tagStart = RsaEncryptedBlockSize + AesGcmNonceSize;
        int tagEnd = tagStart + AesGcmTagSize;
        byte[] tag = envelope[tagStart..tagEnd];
        byte[] ciphertext = envelope[( RsaEncryptedBlockSize + AesGcmNonceSize + AesGcmTagSize )..];

        byte[] aesKey = RSADecrypt(
            rsaEncryptedKey,
            recipientPrivateKey
        );
        return AesGcmDecrypt(
            ciphertext,
            aesKey,
            nonce,
            tag
        );
    }

    // --- Helpers ---

    /// <summary>Generates cryptographically secure random bytes.</summary>
    /// <param name="count">The number of random bytes to generate.</param>
    /// <returns>An array of random bytes.</returns>
    public static byte[] GenerateRandomBytes( int count ) {
        byte[] bytes = new byte[count];
        RandomNumberGenerator.Fill( bytes );
        return bytes;
    }

    /// <summary>
    /// Serializes the public components of an RSA key (Modulus, Exponent) to UTF-8 JSON bytes.
    /// </summary>
    /// <param name="key">The RSA key parameters (public components used).</param>
    /// <returns>UTF-8-encoded JSON bytes containing Modulus and Exponent.</returns>
    public static byte[] SerializePublicKey( RSAParameters key ) {
        var publicOnly = new { key.Modulus, key.Exponent };
        return JsonSerializer.SerializeToUtf8Bytes( publicOnly );
    }

    /// <summary>
    /// Deserializes a public RSA key from UTF-8 JSON bytes produced by <see cref="SerializePublicKey"/>.
    /// </summary>
    /// <param name="data">UTF-8-encoded JSON bytes containing Modulus and Exponent.</param>
    /// <returns>RSA parameters with only public components populated.</returns>
    /// <exception cref="WerkrCryptoException">Thrown when deserialization fails.</exception>
    public static RSAParameters DeserializePublicKey( byte[] data ) {
        try {
            JsonDocument doc = JsonDocument.Parse( data );
            byte[]? modulus = doc.RootElement.GetProperty( "Modulus" ).GetBytesFromBase64( );
            byte[]? exponent = doc.RootElement.GetProperty( "Exponent" ).GetBytesFromBase64( );
            return new RSAParameters { Modulus = modulus, Exponent = exponent };
        } catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) {
            throw new WerkrCryptoException(
                "Failed to deserialize RSA public key from JSON.",
                ex
            );
        }
    }

    // --- Platform Validation ---

    /// <summary>
    /// Validates that the current platform supports all required SHA-512 cryptographic operations.
    /// Performs a real RSA OAEP SHA-512 encrypt/decrypt round-trip.
    /// Throws <see cref="WerkrCryptoException"/> on failure.
    /// Should be called at startup in both Agent and Api.
    /// </summary>
    /// <exception cref="WerkrCryptoException">
    /// Thrown when RSA OAEP SHA-512 is not supported on this platform.
    /// </exception>
    public static void ValidatePlatformCryptoSupport( ) {
        // RSA OAEP SHA-512 round-trip test
        try {
            using RSA testKey = RSA.Create( 2048 );
            RSAParameters pubKey = testKey.ExportParameters( false );
            RSAParameters privKey = testKey.ExportParameters( true );
            byte[] testData = System.Text.Encoding.UTF8.GetBytes( "werkr-platform-validation" );

            byte[] encrypted = testKey.Encrypt(
                testData,
                RSAEncryptionPadding.OaepSHA512
            );

            using RSA decryptKey = RSA.Create( );
            decryptKey.ImportParameters( privKey );
            byte[] decrypted = decryptKey.Decrypt(
                encrypted,
                RSAEncryptionPadding.OaepSHA512
            );

            if (!testData.SequenceEqual( decrypted )) {
                throw new WerkrCryptoException( "RSA OAEP SHA-512 round-trip produced mismatched output." );
            }
        } catch (CryptographicException ex) {
            throw new WerkrCryptoException(
                "RSA OAEP with SHA-512 padding is not supported on this platform.",
                ex
            );
        }
    }

    /// <summary>
    /// Computes the SHA-512 hash of the given data.
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The 64-byte SHA-512 hash.</returns>
    public static byte[] HashSHA512( byte[] data ) {
        return SHA512.HashData( data );
    }

    /// <summary>
    /// Computes the SHA-512 hash of a UTF-8 string.
    /// </summary>
    /// <param name="input">The string to hash.</param>
    /// <returns>The hash as a lowercase hex string.</returns>
    public static string HashSHA512String( string input ) {
        byte[] hash = SHA512.HashData( System.Text.Encoding.UTF8.GetBytes( input ) );
        return Convert.ToHexString( hash );
    }

    /// <summary>
    /// Computes the SHA-256 fingerprint of an RSA public key string.
    /// </summary>
    /// <param name="publicKeyXml">Serialized public key string.</param>
    /// <returns>Lowercase hex-formatted SHA-256 fingerprint.</returns>
    public static string ComputeKeyFingerprint( string publicKeyXml ) {
        byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes( publicKeyXml );
        byte[] hash = SHA256.HashData( keyBytes );
        return Convert.ToHexString( hash ).ToLowerInvariant( );
    }
}
