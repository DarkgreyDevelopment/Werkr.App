namespace Werkr.Data.Entities.Triggers;

/// <summary>
/// Controls how a trigger resolves which workflow version to execute.
/// </summary>
public enum VersionBindingMode {
    /// <summary>Always execute the latest workflow version.</summary>
    Latest = 0,

    /// <summary>Execute a specific pinned workflow version.</summary>
    Pinned = 1,
}
