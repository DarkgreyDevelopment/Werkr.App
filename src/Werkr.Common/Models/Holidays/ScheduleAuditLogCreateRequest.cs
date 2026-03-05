namespace Werkr.Common.Models.Holidays;

/// <summary>
/// Request body for recording a schedule audit log entry
/// when an occurrence is suppressed by a holiday.
/// </summary>
public sealed record ScheduleAuditLogCreateRequest(
    DateTime OccurrenceUtcTime,
    string HolidayName,
    string Reason
);
