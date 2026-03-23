using System.Text.RegularExpressions;

namespace Werkr.Data;

/// <summary>
/// Resolves timezone identifiers to <see cref="TimeZoneInfo"/> instances,
/// with fallback support for fixed-offset IDs (e.g. <c>UTC+05:30</c>).
/// </summary>
public static partial class TimeZoneResolver {

    [GeneratedRegex( @"^UTC([+-]\d{1,2})(?::(\d{2}))?$" )]
    private static partial Regex FixedOffsetPattern( );

    /// <summary>
    /// Finds or creates a <see cref="TimeZoneInfo"/> for the given identifier.
    /// Supports both system timezone IDs (IANA/Windows) and fixed-offset IDs
    /// in the format <c>UTC±HH</c> or <c>UTC±HH:MM</c>.
    /// </summary>
    /// <exception cref="TimeZoneNotFoundException">Thrown when the ID is neither a known system timezone nor a valid fixed-offset format.</exception>
    public static TimeZoneInfo FindOrCreate( string id ) {
        try {
            return TimeZoneInfo.FindSystemTimeZoneById( id );
        } catch (TimeZoneNotFoundException) {
            Match match = FixedOffsetPattern( ).Match( id );
            if (!match.Success) {
                throw;
            }

            int hours = int.Parse( match.Groups[1].Value );
            int minutes = match.Groups[2].Success ? int.Parse( match.Groups[2].Value ) : 0;

            // Preserve sign for minutes (e.g. UTC-5:30 means -5 hours and -30 minutes)
            if (hours < 0) {
                minutes = -minutes;
            }

            TimeSpan offset = new( hours, minutes, 0 );
            return TimeZoneInfo.CreateCustomTimeZone( id, offset, id, id );
        }
    }
}
