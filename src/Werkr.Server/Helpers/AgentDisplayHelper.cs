namespace Werkr.Server.Helpers;

/// <summary>
/// Consolidated UI helpers for agent display formatting.
/// </summary>
public static class AgentDisplayHelper {

    /// <summary>
    /// Formats an operator availability nullable boolean as a display string.
    /// </summary>
    public static string FormatAvailability( bool? value ) =>
        value switch {
            true => "Available",
            false => "Unavailable",
            null => "Unknown"
        };

    /// <summary>
    /// Formats a nullable UTC timestamp as a human-readable relative time string.
    /// </summary>
    public static string FormatRelativeTime( DateTime? timestamp ) {
        if (!timestamp.HasValue) {
            return "Never";
        }

        TimeSpan elapsed = DateTime.UtcNow - timestamp.Value.ToUniversalTime( );
        return elapsed.TotalMinutes < 1
            ? "Just now"
            : elapsed.TotalHours < 1
            ? $"{Math.Max( 1, (int)elapsed.TotalMinutes )} min ago"
            : elapsed.TotalDays < 1
            ? $"{Math.Max( 1, (int)elapsed.TotalHours )} hr ago"
            : $"{Math.Max( 1, (int)elapsed.TotalDays )} day(s) ago";
    }
}
