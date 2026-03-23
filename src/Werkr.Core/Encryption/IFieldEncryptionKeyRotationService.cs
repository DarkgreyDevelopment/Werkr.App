namespace Werkr.Core.Encryption;

/// <summary>
/// Service for rotating the field-level AES-256-GCM encryption key.
/// </summary>
public interface IFieldEncryptionKeyRotationService {

    /// <summary>Starts a key rotation. Returns status immediately if already in progress.</summary>
    Task<RotationStatus> StartRotationAsync( CancellationToken ct );

    /// <summary>Returns the current rotation progress.</summary>
    Task<RotationStatus> GetStatusAsync( CancellationToken ct );
}
