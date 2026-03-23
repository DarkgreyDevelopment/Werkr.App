namespace Werkr.Core.Scheduling;

/// <summary>
/// An occurrence that was suppressed or shifted by a holiday calendar filter.
/// </summary>
/// <param name="UtcTime">The UTC time of the original occurrence.</param>
/// <param name="HolidayName">The name of the holiday that caused the suppression, or empty for allowlist
/// misses.</param>
/// <param name="Reason">A human-readable reason for the suppression or shift.</param>
/// <param name="ShiftedTo">The shifted-to date when the occurrence was moved (null when suppressed).</param>
/// <param name="Action">"Suppressed" when the occurrence is dropped, "Shifted" when moved to a business day.</param>
public sealed record SuppressedOccurrence(
    DateTime UtcTime,
    string HolidayName,
    string Reason,
    DateTime? ShiftedTo = null,
    string Action = "Suppressed"
);
