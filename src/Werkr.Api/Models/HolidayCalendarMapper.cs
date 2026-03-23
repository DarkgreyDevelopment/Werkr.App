using Werkr.Common.Models.Holidays;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Api.Models;

/// <summary>
/// Bidirectional mapping between Holiday Calendar DTOs and domain entities.
/// </summary>
internal static class HolidayCalendarMapper {

    // ── Entity → DTO ───────────────────────────────────────────────────────────

    /// <summary>Converts a <see cref="HolidayCalendar"/> with loaded collections to a full DTO.</summary>
    public static HolidayCalendarDto ToDto( HolidayCalendar calendar ) =>
        new(
            Id: calendar.Id,
            Name: calendar.Name,
            Description: calendar.Description,
            IsSystemCalendar: calendar.IsSystemCalendar,
            CreatedUtc: calendar.CreatedUtc,
            UpdatedUtc: calendar.UpdatedUtc,
            Rules: calendar.Rules?.Select( ToDto ).ToList( ) ?? [],
            Dates: calendar.Dates?.Select( ToDto ).ToList( ) ?? [] );

    /// <summary>Converts a <see cref="HolidayCalendar"/> to a summary DTO with counts.</summary>
    public static HolidayCalendarSummaryDto ToSummaryDto( HolidayCalendar calendar, int scheduleCount ) =>
        new(
            Id: calendar.Id,
            Name: calendar.Name,
            Description: calendar.Description,
            IsSystemCalendar: calendar.IsSystemCalendar,
            RuleCount: calendar.Rules?.Count ?? 0,
            AttachedScheduleCount: scheduleCount );

    /// <summary>Converts a <see cref="HolidayRule"/> to a DTO.</summary>
    public static HolidayRuleDto ToDto( HolidayRule rule ) =>
        new(
            Id: rule.Id,
            HolidayCalendarId: rule.HolidayCalendarId,
            Name: rule.Name,
            RuleType: rule.RuleType.ToString( ),
            Month: rule.Month,
            Day: rule.Day,
            DayOfWeek: rule.DayOfWeek?.ToString( ),
            WeekNumber: rule.WeekNumber,
            WindowStart: rule.WindowStart?.ToString( "HH:mm:ss" ),
            WindowEnd: rule.WindowEnd?.ToString( "HH:mm:ss" ),
            WindowTimeZoneId: rule.WindowTimeZoneId,
            ObservanceRule: rule.ObservanceRule.ToString( ),
            YearStart: rule.YearStart,
            YearEnd: rule.YearEnd );

    /// <summary>Converts a <see cref="HolidayDate"/> to a DTO.</summary>
    public static HolidayDateDto ToDto( HolidayDate date ) =>
        new(
            Id: date.Id,
            HolidayCalendarId: date.HolidayCalendarId,
            Date: date.Date.ToString( "yyyy-MM-dd" ),
            Name: date.Name,
            Year: date.Year,
            WindowStart: date.WindowStart?.ToString( "HH:mm:ss" ),
            WindowEnd: date.WindowEnd?.ToString( "HH:mm:ss" ),
            WindowTimeZoneId: date.WindowTimeZoneId,
            IsManual: date.IsManual,
            GeneratedByRuleId: date.HolidayRuleId );

    // ── DTO → Entity ───────────────────────────────────────────────────────────

    /// <summary>Creates a <see cref="HolidayCalendar"/> from a create request.</summary>
    public static HolidayCalendar ToEntity( HolidayCalendarCreateRequest request ) =>
        new( ) {
            Name = request.Name,
            Description = request.Description,
        };

    /// <summary>Creates a <see cref="HolidayRule"/> from a create request.</summary>
    public static HolidayRule ToEntity( HolidayRuleCreateRequest request ) =>
        new( ) {
            Name = request.Name,
            RuleType = Enum.Parse<HolidayRuleType>( request.RuleType, ignoreCase: true ),
            Month = request.Month,
            Day = request.Day,
            DayOfWeek = request.DayOfWeek is not null
                ? Enum.Parse<DayOfWeek>( request.DayOfWeek, ignoreCase: true )
                : null,
            WeekNumber = request.WeekNumber,
            WindowStart = request.WindowStart is not null ? TimeOnly.Parse( request.WindowStart ) : null,
            WindowEnd = request.WindowEnd is not null ? TimeOnly.Parse( request.WindowEnd ) : null,
            WindowTimeZoneId = request.WindowTimeZoneId,
            ObservanceRule = Enum.Parse<ObservanceRule>( request.ObservanceRule, ignoreCase: true ),
            YearStart = request.YearStart,
            YearEnd = request.YearEnd,
        };

    /// <summary>Creates a <see cref="HolidayRule"/> from an update request.</summary>
    public static HolidayRule ToEntity( HolidayRuleUpdateRequest request ) =>
        new( ) {
            Name = request.Name,
            RuleType = Enum.Parse<HolidayRuleType>( request.RuleType, ignoreCase: true ),
            Month = request.Month,
            Day = request.Day,
            DayOfWeek = request.DayOfWeek is not null
                ? Enum.Parse<DayOfWeek>( request.DayOfWeek, ignoreCase: true )
                : null,
            WeekNumber = request.WeekNumber,
            WindowStart = request.WindowStart is not null ? TimeOnly.Parse( request.WindowStart ) : null,
            WindowEnd = request.WindowEnd is not null ? TimeOnly.Parse( request.WindowEnd ) : null,
            WindowTimeZoneId = request.WindowTimeZoneId,
            ObservanceRule = Enum.Parse<ObservanceRule>( request.ObservanceRule, ignoreCase: true ),
            YearStart = request.YearStart,
            YearEnd = request.YearEnd,
        };

    /// <summary>Creates a <see cref="HolidayRule"/> from a rule preview request.</summary>
    public static HolidayRule ToEntity( RulePreviewRequest request ) =>
        new( ) {
            Name = request.Name,
            RuleType = Enum.Parse<HolidayRuleType>( request.RuleType, ignoreCase: true ),
            Month = request.Month,
            Day = request.Day,
            DayOfWeek = request.DayOfWeek is not null
                ? Enum.Parse<DayOfWeek>( request.DayOfWeek, ignoreCase: true )
                : null,
            WeekNumber = request.WeekNumber,
            WindowStart = request.WindowStart is not null ? TimeOnly.Parse( request.WindowStart ) : null,
            WindowEnd = request.WindowEnd is not null ? TimeOnly.Parse( request.WindowEnd ) : null,
            WindowTimeZoneId = request.WindowTimeZoneId,
            ObservanceRule = Enum.Parse<ObservanceRule>( request.ObservanceRule, ignoreCase: true ),
            YearStart = request.YearStart,
            YearEnd = request.YearEnd,
        };

    /// <summary>Creates a <see cref="HolidayDate"/> from a create request.</summary>
    public static HolidayDate ToEntity( HolidayDateCreateRequest request ) =>
        new( ) {
            Date = DateOnly.Parse( request.Date ),
            Name = request.Name,
            Year = DateOnly.Parse( request.Date ).Year,
            WindowStart = request.WindowStart is not null ? TimeOnly.Parse( request.WindowStart ) : null,
            WindowEnd = request.WindowEnd is not null ? TimeOnly.Parse( request.WindowEnd ) : null,
            WindowTimeZoneId = request.WindowTimeZoneId,
        };

}
