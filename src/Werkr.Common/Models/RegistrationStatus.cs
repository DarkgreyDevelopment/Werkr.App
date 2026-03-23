namespace Werkr.Common.Models;

/// <summary>Status of a pending registration bundle.</summary>
public enum RegistrationStatus {
    /// <summary>Bundle has been generated and is awaiting completion.</summary>
    Pending = 0,

    /// <summary>Registration has been completed successfully.</summary>
    Completed = 1,

    /// <summary>Bundle has expired without being used.</summary>
    Expired = 2,

    /// <summary>Bundle has been manually revoked by an admin.</summary>
    Revoked = 3,
}
