namespace Werkr.Core.Scheduling;

/// <summary>
/// An occurrence that was suppressed (or, in allowlist mode, not matched) by a holiday calendar filter.
/// </summary>
/// <param name="UtcTime">The UTC time of the suppressed occurrence.</param>
/// <param name="HolidayName">The name of the holiday that caused the suppression, or empty for allowlist misses.</param>
/// <param name="Reason">A human-readable reason for the suppression.</param>
public sealed record SuppressedOccurrence(
    DateTime UtcTime,
    string HolidayName,
    string Reason );
