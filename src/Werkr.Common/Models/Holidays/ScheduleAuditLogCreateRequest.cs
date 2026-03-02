namespace Werkr.Common.Models.Holidays;

/// <summary>Create-audit-log request.</summary>
public sealed record ScheduleAuditLogCreateRequest(
    DateTime OccurrenceUtcTime,
    string HolidayName,
    string Reason );
