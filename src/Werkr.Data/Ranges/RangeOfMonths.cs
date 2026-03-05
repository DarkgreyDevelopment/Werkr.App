using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Extensions;

namespace Werkr.Data.Ranges;

/// <summary>
/// Produces contiguous range descriptions for <see cref="MonthsOfYear"/> flags
/// (e.g., "Jan-Mar, Jun").
/// </summary>
public class RangeOfMonths : IRange<Month> {
    private int _start = 1;
    private int _end = 12;

    /// <inheritdoc/>
    public Month Start => (Month)_start;

    /// <inheritdoc/>
    public Month End => (Month)_end;

    /// <summary>Sets the start month index.</summary>
    public void SetStart( int start ) => _start = start;

    /// <summary>Sets the end month index.</summary>
    public void SetEnd( int end ) => _end = end;

    /// <inheritdoc/>
    public override string ToString( ) =>
        _start == _end
            ? Start.ToString( true )
            : $"{Start.ToString( true )} - {End.ToString( true )}";

    /// <summary>
    /// Formats this range as a string using either abbreviated or full month names.
    /// </summary>
    /// <param name="abbreviated">
    /// When <see langword="true"/>, uses abbreviations (e.g., "Jan").
    /// When <see langword="false"/>, uses full names (e.g., "January").
    /// </param>
    public string ToString( bool abbreviated = false ) =>
        _start == _end
            ? Start.ToString( abbreviated )
            : $"{Start.ToString( abbreviated )} - {End.ToString( abbreviated )}";

    /// <summary>
    /// Formats a collection of <see cref="RangeOfMonths"/> as a comma-separated string.
    /// </summary>
    public static string ToString( IEnumerable<RangeOfMonths> ranges, bool abbreviated = false )
        => string.Join(
            ", ",
            ranges
                .OrderBy( item => item._start )
                .Select( item => item.ToString( abbreviated ) ) );

    /// <summary>
    /// Converts a list of <see cref="Month"/> values into contiguous ranges.
    /// </summary>
    public static IEnumerable<RangeOfMonths> GetContiguousRanges( List<Month> months ) {
        List<RangeOfMonths> ranges = [];
        RangeOfMonths range = new( );
        foreach (IntRange monthRange in IntRange.GetContiguousRanges( months.GetIntMonths( ) )) {
            range.SetStart( monthRange.Start );
            range.SetEnd( monthRange.End );
            ranges.Add( range );
            range = new( );
        }
        return ranges;
    }

    /// <summary>
    /// Converts <see cref="MonthsOfYear"/> flags into contiguous ranges.
    /// </summary>
    public static IEnumerable<RangeOfMonths> GetContiguousRanges( MonthsOfYear months )
        => GetContiguousRanges( months.GetMonths( ) );
}
