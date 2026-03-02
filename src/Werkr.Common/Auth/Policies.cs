namespace Werkr.Common.Auth;

/// <summary>
/// Constants for permission-based authorization policy names.
/// Use with <c>[Authorize( Policy = Policies.CanRead )]</c> on Blazor pages and endpoints.
/// </summary>
public static class Policies {
    /// <summary>User can create entities (schedules, tasks, workflows).</summary>
    public const string CanCreate = "CanCreate";

    /// <summary>User can view/read entities and data.</summary>
    public const string CanRead = "CanRead";

    /// <summary>User can modify existing entities.</summary>
    public const string CanUpdate = "CanUpdate";

    /// <summary>User can delete entities.</summary>
    public const string CanDelete = "CanDelete";

    /// <summary>User can execute tasks, run workflows, manage agent operations.</summary>
    public const string CanExecute = "CanExecute";

    /// <summary>User has full administrative access.</summary>
    public const string IsAdmin = "IsAdmin";
}
