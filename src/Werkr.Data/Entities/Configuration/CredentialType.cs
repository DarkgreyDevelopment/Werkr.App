namespace Werkr.Data.Entities.Configuration;

/// <summary>
/// Type classification for stored credentials.
/// </summary>
public enum CredentialType {
    /// <summary>Username/password pair.</summary>
    Password = 0,

    /// <summary>API key or bearer token.</summary>
    ApiKey = 1,

    /// <summary>Database or service connection string.</summary>
    ConnectionString = 2,

    /// <summary>Certificate (PEM or PFX, base64-encoded).</summary>
    Certificate = 3,
}
