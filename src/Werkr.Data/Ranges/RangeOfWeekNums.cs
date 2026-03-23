using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Extensions;

namespace Werkr.Data.Ranges;

/// <summary>
/// Produces contiguous range descriptions for <see cref="WeekNumberWithinMonth"/> flags
/// (e.g., "1st-3rd, 5th").
/// </summary>
public class RangeOfWeekNums : IRange<WeekNumberWithinMonth> {

    private int _start;
    private int _end;

    /// <inheritdoc/>
    public WeekNumberWithinMonth Start => (WeekNumberWithinMonth)_start;

    /// <inheritdoc/>
    public WeekNumberWithinMonth End => (WeekNumberWithinMonth)_end;

    /// <summary>Sets the start week number index.</summary>
    public void SetStart( int start ) => _start = start;

    /// <summary>Sets the end week number index.</summary>
    public void SetEnd( int end ) => _end = end;

    /// <inheritdoc/>
    public override string ToString( ) =>
        _start == _end
            ? Start.ToString( )
            : $"{Start} - {End}";

    /// <summary>
    /// Formats this range as a string.
    /// </summary>
    /// <param name="abbreviated">
    /// When <see langword="true"/>, uses numeric representation (e.g., "1").
    /// When <see langword="false"/>, uses ordinal names (e.g., "First").
    /// </param>
    public string ToString( bool abbreviated = false ) {
        string start = Start.ToString( );
        string end = End.ToString( );
        if (abbreviated) {
            start = GetWeekNum( Start ).ToString( );
            end = GetWeekNum( End ).ToString( );
        }
        return _start == _end
            ? start
            : $"{start} - {end}";
    }

    /// <summary>
    /// Formats a collection of <see cref="RangeOfWeekNums"/> as a comma-separated string.
    /// </summary>
    public static string ToString( IEnumerable<RangeOfWeekNums> ranges, bool abbreviated = false )
        => string.Join(
            ", ",
            ranges
                .OrderBy( item => item._start )
                .Select( item => item.ToString( abbreviated ) ) );

    /// <summary>
    /// Converts <see cref="WeekNumberWithinMonth"/> flags into contiguous ranges.
    /// </summary>
    public static IEnumerable<RangeOfWeekNums> GetContiguousRanges( WeekNumberWithinMonth weekNums ) {
        List<RangeOfWeekNums> ranges = [];
        RangeOfWeekNums range = new( );
        foreach (IntRange weekRange in IntRange.GetContiguousRanges( weekNums.GetWeekNumbersInMonth( ) )) {
            range.SetStart( GetWeekNum( weekRange.Start ) );
            range.SetEnd( GetWeekNum( weekRange.End ) );
            ranges.Add( range );
            range = new( );
        }
        return ranges;
    }

    /// <summary>
    /// Converts a 1-based week number integer to the corresponding
    /// <see cref="WeekNumberWithinMonth"/> flag value.
    /// </summary>
    internal static int GetWeekNum( int weekNumberWithinMonth )
        => weekNumberWithinMonth switch {
            1 => (int)WeekNumberWithinMonth.First,
            2 => (int)WeekNumberWithinMonth.Second,
            3 => (int)WeekNumberWithinMonth.Third,
            4 => (int)WeekNumberWithinMonth.Fourth,
            5 => (int)WeekNumberWithinMonth.Fifth,
            6 => (int)WeekNumberWithinMonth.Sixth,
            _ => throw new InvalidOperationException( "Invalid Week Number" ),
        };

    /// <summary>
    /// Converts a <see cref="WeekNumberWithinMonth"/> flag value to its
    /// 1-based week number integer equivalent.
    /// </summary>
    internal static int GetWeekNum( WeekNumberWithinMonth weekNumberWithinMonth )
        => weekNumberWithinMonth switch {
            WeekNumberWithinMonth.First => 1,
            WeekNumberWithinMonth.Second => 2,
            WeekNumberWithinMonth.Third => 3,
            WeekNumberWithinMonth.Fourth => 4,
            WeekNumberWithinMonth.Fifth => 5,
            WeekNumberWithinMonth.Sixth => 6,
            _ => throw new InvalidOperationException( "Invalid Week Number" ),
        };
}
