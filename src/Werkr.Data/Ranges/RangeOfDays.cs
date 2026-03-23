using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Extensions;

namespace Werkr.Data.Ranges;

/// <summary>
/// Produces contiguous range descriptions for <see cref="DaysOfWeek"/> flags
/// (e.g., "Mon-Wed, Fri").
/// </summary>
public class RangeOfDays : IRange<DayOfWeek> {

    private int _start;
    private int _end = 6;

    /// <inheritdoc/>
    public DayOfWeek Start => (DayOfWeek)_start;

    /// <inheritdoc/>
    public DayOfWeek End => (DayOfWeek)_end;

    /// <summary>Sets the start day index.</summary>
    public void SetStart( int start ) => _start = start;

    /// <summary>Sets the end day index.</summary>
    public void SetEnd( int end ) => _end = end;

    /// <inheritdoc/>
    public override string ToString( ) =>
        _start == _end
            ? Start.ToString( true )
            : $"{Start.ToString( true )} - {End.ToString( true )}";

    /// <summary>
    /// Formats this range as a string using either abbreviated or full day names.
    /// </summary>
    /// <param name="abbreviated">
    /// When <see langword="true"/>, uses abbreviations (e.g., "Mon").
    /// When <see langword="false"/>, uses full names (e.g., "Monday").
    /// </param>
    public string ToString( bool abbreviated = false ) =>
        _start == _end
            ? Start.ToString( abbreviated )
            : $"{Start.ToString( abbreviated )} - {End.ToString( abbreviated )}";

    /// <summary>
    /// Formats a collection of <see cref="RangeOfDays"/> as a comma-separated string.
    /// </summary>
    public static string ToString( IEnumerable<RangeOfDays> ranges, bool abbreviated = false )
        => string.Join(
            ", ",
            ranges
                .OrderBy( item => item._start )
                .Select( item => item.ToString( abbreviated ) ) );

    /// <summary>
    /// Converts a list of <see cref="DayOfWeek"/> values into contiguous ranges.
    /// </summary>
    public static IEnumerable<RangeOfDays> GetContiguousRanges( List<DayOfWeek> days ) {
        List<RangeOfDays> ranges = [];
        RangeOfDays range = new( );
        foreach (IntRange intRange in IntRange.GetContiguousRanges( days.Select( day => (int)day ) )) {
            range.SetStart( intRange.Start );
            range.SetEnd( intRange.End );
            ranges.Add( range );
            range = new( );
        }
        return ranges;
    }

    /// <summary>
    /// Converts <see cref="DaysOfWeek"/> flags into contiguous ranges.
    /// </summary>
    public static IEnumerable<RangeOfDays> GetContiguousRanges( DaysOfWeek days )
        => GetContiguousRanges( days.GetDaysOfWeek( ) );
}
