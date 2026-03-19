namespace Werkr.Server.Helpers;

/// <summary>
/// Centralized mapping of domain status strings to Werkr CSS badge classes.
/// All returned classes reference CSS custom properties defined in theme.css.
/// </summary>
public static class StatusDisplayHelper {

    /// <summary>
    /// Returns the Werkr CSS badge class for a given status string.
    /// All returned classes reference CSS custom properties defined in theme.css.
    /// </summary>
    public static string GetStatusBadgeClass( string? status ) =>
        status?.Trim( ).ToLowerInvariant( ) switch {
            // Step/run execution statuses
            "succeeded" or "success" => "werkr-status-succeeded",
            "failed" or "faulted" or "error" => "werkr-status-failed",
            "running" or "inprogress" or "in-progress" or "in_progress" or "executing" => "werkr-status-running",
            "pending" => "werkr-status-pending",
            "skipped" => "werkr-status-skipped",
            "cancelled" or "canceled" => "werkr-status-cancelled",

            // Non-execution domain statuses (agents, connections, config)
            "ready" or "connected" or "active" or "enabled" or "healthy" => "werkr-status-succeeded",
            "unreachable" => "werkr-status-failed",
            "disconnected" => "werkr-status-running",
            "disabled" or "revoked" or "expired" or "registered" or "queued" => "werkr-status-skipped",

            _ => "werkr-status-skipped",
        };

}
