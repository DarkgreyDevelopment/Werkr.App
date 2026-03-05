namespace Werkr.Common.Models.Holidays;

/// <summary>
/// Audit log record documenting a suppressed schedule occurrence
/// and the holiday that caused the suppression.
/// </summary>
public sealed record ScheduleAuditLogDto(
    long Id,
    Guid ScheduleId,
    DateTime OccurrenceUtcTime,
    string CalendarName,
    string HolidayName,
    string Mode,
    DateTime CreatedUtc
);
