using TimeZoneNames;

namespace Werkr.Common.Scheduling;

/// <summary>
/// Provides DST-aware timezone abbreviations, localized display names,
/// fixed-offset labels, and dropdown list generation.
/// Wraps the <c>TimeZoneNames</c> CLDR-based package.
/// </summary>
public static class TimeZoneDisplayService {

    /// <summary>
    /// Returns the DST-aware abbreviation for the given timezone at the specified instant.
    /// For fixed-offset timezones, returns a GMT offset label instead.
    /// </summary>
    public static string GetAbbreviation( TimeZoneInfo tz, DateTime instant, string languageCode = "en" ) {
        if (tz.Equals( TimeZoneInfo.Utc )) {
            return "UTC";
        }

        // Fixed-offset timezones have no adjustment rules and a non-zero offset
        if (IsFixedOffset( tz )) {
            return GetFixedOffsetLabel( tz.BaseUtcOffset );
        }

        TimeZoneValues? abbreviations = TZNames.GetAbbreviationsForTimeZone( tz.Id, languageCode );
        if (abbreviations is null) {
            return tz.StandardName;
        }

        bool isDst = tz.IsDaylightSavingTime( instant );
        string? abbrev = isDst ? abbreviations.Daylight : abbreviations.Standard;
        return abbrev ?? abbreviations.Generic ?? tz.StandardName;
    }

    /// <summary>
    /// Returns the localized display name for the given timezone.
    /// </summary>
    public static string GetDisplayName( TimeZoneInfo tz, string languageCode = "en" ) {
        TimeZoneValues? names = TZNames.GetNamesForTimeZone( tz.Id, languageCode );
        return names?.Generic ?? names?.Standard ?? tz.DisplayName;
    }

    /// <summary>
    /// Formats a <see cref="TimeSpan"/> offset as a GMT label (e.g. <c>GMT+5:30</c>, <c>GMT-7</c>).
    /// </summary>
    public static string GetFixedOffsetLabel( TimeSpan offset ) {
        if (offset == TimeSpan.Zero) {
            return "GMT+0";
        }

        string sign = offset < TimeSpan.Zero ? "-" : "+";
        TimeSpan abs = offset.Duration( );
        int hours = abs.Hours + ( abs.Days * 24 );
        int minutes = abs.Minutes;

        return minutes == 0
            ? $"GMT{sign}{hours}"
            : $"GMT{sign}{hours}:{minutes:D2}";
    }

    /// <summary>
    /// Returns a list of system timezones with localized display names and current abbreviations.
    /// </summary>
    public static IReadOnlyList<TimeZoneListItem> GetTimeZoneListItems( string languageCode = "en" ) {
        DateTime now = DateTime.UtcNow;
        return TimeZoneInfo.GetSystemTimeZones( )
            .OrderBy( tz => tz.BaseUtcOffset )
            .Select( tz => {
                string offsetLabel = FormatUtcOffset( tz.BaseUtcOffset );
                string displayName = GetDisplayName( tz, languageCode );
                string abbreviation = GetAbbreviation( tz, now, languageCode );
                return new TimeZoneListItem( tz.Id, $"({offsetLabel}) {displayName}", abbreviation, tz.BaseUtcOffset );
            } )
            .ToList( );
    }

    /// <summary>
    /// Returns the canonical set of fixed UTC offsets from UTC-12:00 through UTC+14:00,
    /// including half-hour (:30) and quarter-hour (:45) increments.
    /// </summary>
    public static IReadOnlyList<FixedOffsetListItem> GetFixedOffsetListItems( ) {
        List<FixedOffsetListItem> items = [];

        // UTC-12:00 through UTC+14:00 in 30-minute increments, plus :45 specials
        for (int totalMinutes = -720; totalMinutes <= 840; totalMinutes += 30) {
            TimeSpan offset = TimeSpan.FromMinutes( totalMinutes );
            items.Add( new FixedOffsetListItem(
                FormatFixedOffsetId( offset ),
                GetFixedOffsetLabel( offset ),
                offset
            ) );
        }

        // Add quarter-hour offsets: UTC+5:45, UTC+12:45, etc.
        int[] quarterHourOffsets = [345, 765]; // 5:45, 12:45 in minutes
        foreach (int mins in quarterHourOffsets) {
            TimeSpan offset = TimeSpan.FromMinutes( mins );
            FixedOffsetListItem item = new(
                FormatFixedOffsetId( offset ),
                GetFixedOffsetLabel( offset ),
                offset
            );
            // Insert in sorted position
            int insertIndex = items.FindIndex( i => i.Offset > offset );
            if (insertIndex >= 0) {
                items.Insert( insertIndex, item );
            } else {
                items.Add( item );
            }
        }

        return items;
    }

    private static bool IsFixedOffset( TimeZoneInfo tz ) =>
        tz.GetAdjustmentRules( ).Length == 0 && tz.BaseUtcOffset != TimeSpan.Zero;

    private static string FormatUtcOffset( TimeSpan offset ) {
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        TimeSpan abs = offset.Duration( );
        int hours = abs.Hours + ( abs.Days * 24 );
        int minutes = abs.Minutes;
        return minutes == 0
            ? $"UTC{sign}{hours:D2}:00"
            : $"UTC{sign}{hours:D2}:{minutes:D2}";
    }

    private static string FormatFixedOffsetId( TimeSpan offset ) {
        if (offset == TimeSpan.Zero) {
            return "UTC";
        }

        string sign = offset < TimeSpan.Zero ? "-" : "+";
        TimeSpan abs = offset.Duration( );
        int hours = abs.Hours + ( abs.Days * 24 );
        int minutes = abs.Minutes;
        return minutes == 0
            ? $"UTC{sign}{hours}"
            : $"UTC{sign}{hours}:{minutes:D2}";
    }
}

/// <summary>Represents a system timezone for dropdown display.</summary>
/// <param name="Id">The timezone identifier (IANA or Windows).</param>
/// <param name="DisplayName">Localized display name with UTC offset prefix.</param>
/// <param name="Abbreviation">Current DST-aware abbreviation.</param>
/// <param name="Offset">Base UTC offset.</param>
public sealed record TimeZoneListItem( string Id, string DisplayName, string Abbreviation, TimeSpan Offset );

/// <summary>Represents a fixed UTC offset for dropdown display.</summary>
/// <param name="Id">The fixed-offset timezone identifier (e.g. <c>UTC+5:30</c>).</param>
/// <param name="Label">Display label (e.g. <c>GMT+5:30</c>).</param>
/// <param name="Offset">The UTC offset.</param>
public sealed record FixedOffsetListItem( string Id, string Label, TimeSpan Offset );
