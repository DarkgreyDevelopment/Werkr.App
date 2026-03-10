using Werkr.Data.Calendar.Enums;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Data.Seeding;

/// <summary>
/// Seeds system holiday calendars (US Federal Holidays, Federal Reserve Holidays)
/// on application startup. Idempotent - checks by name before inserting.
/// </summary>
public static class HolidayCalendarSeeder {
    // Deterministic GUIDs for system calendar IDs (stable across environments)
    private static readonly Guid s_usFederalCalendarId =
        new( "A0000001-0000-0000-0000-000000000001" );
    private static readonly Guid s_fedReserveCalendarId =
        new( "A0000001-0000-0000-0000-000000000002" );

    /// <summary>
    /// Seeds default system holiday calendars if they do not already exist.
    /// Called during application startup.
    /// </summary>
    /// <param name="services">The application's root <see cref="IServiceProvider"/>.</param>
    public static async Task SeedAsync( IServiceProvider services ) {
        using IServiceScope scope = services.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        ILogger logger = services.GetRequiredService<ILoggerFactory>( )
            .CreateLogger( "Werkr.Data.Seeding.HolidayCalendarSeeder" );

        await SeedUsFederalHolidaysAsync( db, logger );
        await SeedFedReserveHolidaysAsync( db, logger );
    }

    private static async Task SeedUsFederalHolidaysAsync( WerkrDbContext db, ILogger logger ) {
        bool exists = await db.HolidayCalendars
            .AnyAsync( c => c.Name == "US Federal Holidays" );
        if (exists) {
            return;
        }

        HolidayCalendar calendar = new( ) {
            Id = s_usFederalCalendarId,
            Name = "US Federal Holidays",
            Description = "Official US federal holidays observed by the federal government.",
            IsSystemCalendar = true,
        };
        _ = db.HolidayCalendars.Add( calendar );

        HolidayRule[] rules = GetUsFederalRules( s_usFederalCalendarId );
        db.HolidayRules.AddRange( rules );

        _ = await db.SaveChangesAsync( );
        LogCalendarSeeded( logger, "US Federal Holidays", rules.Length );
    }

    private static async Task SeedFedReserveHolidaysAsync( WerkrDbContext db, ILogger logger ) {
        bool exists = await db.HolidayCalendars
            .AnyAsync( c => c.Name == "Federal Reserve Holidays" );
        if (exists) {
            return;
        }

        HolidayCalendar calendar = new( ) {
            Id = s_fedReserveCalendarId,
            Name = "Federal Reserve Holidays",
            Description = "Holidays observed by the US Federal Reserve System (excludes Columbus Day).",
            IsSystemCalendar = true,
        };
        _ = db.HolidayCalendars.Add( calendar );

        // Same as US Federal minus Columbus Day (Decision H13)
        HolidayRule[] rules = GetFedReserveRules( s_fedReserveCalendarId );
        db.HolidayRules.AddRange( rules );

        _ = await db.SaveChangesAsync( );
        LogCalendarSeeded( logger, "Federal Reserve Holidays", rules.Length );
    }

    private static readonly Action<ILogger, string, int, Exception?> s_logCalendarSeeded =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId( 1, "CalendarSeeded" ),
            "Seeded system calendar: {CalendarName} ({RuleCount} rules)" );

    private static void LogCalendarSeeded( ILogger logger, string calendarName, int ruleCount ) {
        s_logCalendarSeeded( logger, calendarName, ruleCount, null );
    }

    /// <summary>
    /// Builds the 11 US Federal Holiday rules.
    /// </summary>
    private static HolidayRule[] GetUsFederalRules( Guid calendarId ) => [
        // New Year's Day — January 1
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "New Year's Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 1,
            Day = 1,
            ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday,
        },
        // MLK Jr. Day — 3rd Monday in January
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Martin Luther King Jr. Day",
            RuleType = HolidayRuleType.NthWeekdayOfMonth,
            Month = 1,
            DayOfWeek = DayOfWeek.Monday,
            WeekNumber = 3,
            ObservanceRule = ObservanceRule.None,
        },
        // Presidents' Day — 3rd Monday in February
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Presidents' Day",
            RuleType = HolidayRuleType.NthWeekdayOfMonth,
            Month = 2,
            DayOfWeek = DayOfWeek.Monday,
            WeekNumber = 3,
            ObservanceRule = ObservanceRule.None,
        },
        // Memorial Day — Last Monday in May
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Memorial Day",
            RuleType = HolidayRuleType.LastWeekdayOfMonth,
            Month = 5,
            DayOfWeek = DayOfWeek.Monday,
            ObservanceRule = ObservanceRule.None,
        },
        // Juneteenth — June 19 (since 2021)
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Juneteenth National Independence Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 6,
            Day = 19,
            ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday,
            YearStart = 2021,
        },
        // Independence Day — July 4
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Independence Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 7,
            Day = 4,
            ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday,
        },
        // Labor Day — 1st Monday in September
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Labor Day",
            RuleType = HolidayRuleType.NthWeekdayOfMonth,
            Month = 9,
            DayOfWeek = DayOfWeek.Monday,
            WeekNumber = 1,
            ObservanceRule = ObservanceRule.None,
        },
        // Columbus Day — 2nd Monday in October
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Columbus Day",
            RuleType = HolidayRuleType.NthWeekdayOfMonth,
            Month = 10,
            DayOfWeek = DayOfWeek.Monday,
            WeekNumber = 2,
            ObservanceRule = ObservanceRule.None,
        },
        // Veterans Day — November 11
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Veterans Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 11,
            Day = 11,
            ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday,
        },
        // Thanksgiving — 4th Thursday in November
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Thanksgiving Day",
            RuleType = HolidayRuleType.NthWeekdayOfMonth,
            Month = 11,
            DayOfWeek = DayOfWeek.Thursday,
            WeekNumber = 4,
            ObservanceRule = ObservanceRule.None,
        },
        // Christmas Day — December 25
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Christmas Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 12,
            Day = 25,
            ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday,
        },
    ];

    /// <summary>
    /// Builds the 10 Federal Reserve Holiday rules (same as US Federal minus Columbus Day).
    /// </summary>
    private static HolidayRule[] GetFedReserveRules( Guid calendarId ) => [
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "New Year's Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 1, Day = 1,
            ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday,
        },
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Martin Luther King Jr. Day",
            RuleType = HolidayRuleType.NthWeekdayOfMonth,
            Month = 1, DayOfWeek = DayOfWeek.Monday, WeekNumber = 3,
            ObservanceRule = ObservanceRule.None,
        },
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Presidents' Day",
            RuleType = HolidayRuleType.NthWeekdayOfMonth,
            Month = 2, DayOfWeek = DayOfWeek.Monday, WeekNumber = 3,
            ObservanceRule = ObservanceRule.None,
        },
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Memorial Day",
            RuleType = HolidayRuleType.LastWeekdayOfMonth,
            Month = 5, DayOfWeek = DayOfWeek.Monday,
            ObservanceRule = ObservanceRule.None,
        },
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Juneteenth National Independence Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 6, Day = 19,
            ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday,
            YearStart = 2021,
        },
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Independence Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 7, Day = 4,
            ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday,
        },
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Labor Day",
            RuleType = HolidayRuleType.NthWeekdayOfMonth,
            Month = 9, DayOfWeek = DayOfWeek.Monday, WeekNumber = 1,
            ObservanceRule = ObservanceRule.None,
        },
        // Columbus Day OMITTED per Decision H13
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Veterans Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 11, Day = 11,
            ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday,
        },
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Thanksgiving Day",
            RuleType = HolidayRuleType.NthWeekdayOfMonth,
            Month = 11, DayOfWeek = DayOfWeek.Thursday, WeekNumber = 4,
            ObservanceRule = ObservanceRule.None,
        },
        new( ) {
            HolidayCalendarId = calendarId,
            Name = "Christmas Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 12, Day = 25,
            ObservanceRule = ObservanceRule.SaturdayToFriday_SundayToMonday,
        },
    ];
}
