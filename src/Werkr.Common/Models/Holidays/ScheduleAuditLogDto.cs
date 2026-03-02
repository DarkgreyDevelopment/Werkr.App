namespace Werkr.Common.Models.Holidays;

/// <summary>Full audit log record.</summary>
public sealed record ScheduleAuditLogDto(
    long Id,
    Guid ScheduleId,
    DateTime OccurrenceUtcTime,
    string CalendarName,
    string HolidayName,
    string Mode,
    DateTime CreatedUtc );
