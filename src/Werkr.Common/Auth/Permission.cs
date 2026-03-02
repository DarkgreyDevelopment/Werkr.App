namespace Werkr.Common.Auth;

/// <summary>
/// Coarse-grained permission types for the Werkr application.
/// Designed for expansion to fine-grained permissions in a future phase.
/// </summary>
public enum Permission {
    /// <summary>Can create new entities (schedules, tasks, workflows).</summary>
    Create = 0,

    /// <summary>Can read/view entities and data.</summary>
    Read = 1,

    /// <summary>Can modify existing entities.</summary>
    Update = 2,

    /// <summary>Can remove entities.</summary>
    Delete = 3,

    /// <summary>Can execute tasks, run workflows, and manage agent operations.</summary>
    Execute = 4,

    /// <summary>Full administrative access (user management, settings, roles/permissions).</summary>
    Admin = 5,
}
