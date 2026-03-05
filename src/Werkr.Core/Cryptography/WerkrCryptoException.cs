namespace Werkr.Core.Cryptography;

/// <summary>
/// Exception type for all Werkr cryptographic operation failures.
/// Wraps platform-level <see cref="System.Security.Cryptography.CryptographicException"/>
/// with clear, actionable error messages.
/// </summary>
public class WerkrCryptoException : Exception {
    /// <summary>Creates a new <see cref="WerkrCryptoException"/> with the specified message.</summary>
    /// <param name="message">A clear description of what went wrong.</param>
    public WerkrCryptoException( string message ) : base( message ) { }

    /// <summary>Creates a new <see cref="WerkrCryptoException"/> with the specified message and inner
    /// exception.</summary>
    /// <param name="message">A clear description of what went wrong.</param>
    /// <param name="inner">The underlying exception that caused this failure.</param>
    public WerkrCryptoException(
        string message,
        Exception inner
    ) : base(
        message,
        inner
    ) { }
}
