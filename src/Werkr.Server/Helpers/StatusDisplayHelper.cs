namespace Werkr.Server.Helpers;

/// <summary>
/// Centralized mapping of domain status strings to Bootstrap CSS badge classes.
/// Replaces duplicated inline helpers across pages.
/// </summary>
public static class StatusDisplayHelper {

    /// <summary>
    /// Returns the Bootstrap badge CSS class for a given status string.
    /// Handles all known status strings across agents, jobs, workflow runs,
    /// schedules, and API keys.
    /// </summary>
    public static string GetStatusBadgeClass( string? status ) =>
        status?.Trim( ).ToLowerInvariant( ) switch {
            // Success family
            "succeeded" or "success" or "ready" or "connected" or "active" or "enabled" or "healthy" => "bg-success",

            // Failure family
            "failed" or "faulted" or "error" or "unreachable" => "bg-danger",

            // Running / in-progress family
            "running" or "inprogress" or "in-progress" or "in_progress" or "executing" => "bg-warning text-dark",

            // Pending family
            "pending" or "queued" or "registered" => "bg-primary",

            // Skipped / inactive family
            "skipped" or "disabled" or "revoked" or "expired" or "cancelled" or "canceled" => "bg-secondary",

            // Disconnected — distinct from failed (recoverable)
            "disconnected" => "bg-warning text-dark",

            // Unknown / default
            _ => "bg-secondary",
        };

}
