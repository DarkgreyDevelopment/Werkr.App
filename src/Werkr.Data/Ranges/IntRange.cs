namespace Werkr.Data.Ranges;

/// <summary>
/// Represents a contiguous range of integers. Used for formatting
/// day-of-month lists and other numeric sequences into human-readable descriptions
/// (e.g., "1–5, 15, 20–25").
/// </summary>
public class IntRange : IRange<int> {
    private const long DefaultStartValue = (long)int.MinValue - 1;
    private const long DefaultEndValue = (long)int.MaxValue + 1;

    private long _start = DefaultStartValue;
    private long _end = DefaultEndValue;

    /// <inheritdoc/>
    public int Start => _start == DefaultStartValue ? int.MinValue : (int)_start;

    /// <inheritdoc/>
    public int End => _end == DefaultEndValue ? int.MaxValue : (int)_end;

    /// <summary>Initializes a new range with default (sentinel) values.</summary>
    public IntRange( ) {
        _start = DefaultStartValue;
        _end = DefaultEndValue;
    }

    /// <summary>Initializes a range with explicit start and end values.</summary>
    /// <param name="start">The inclusive start of the range.</param>
    /// <param name="end">The inclusive end of the range.</param>
    public IntRange( int start, int end ) {
        _start = start;
        _end = end;
    }

    /// <summary>Sets the start value of this range.</summary>
    public void SetStart( int start ) => _start = start;

    /// <summary>Sets the end value of this range.</summary>
    public void SetEnd( int end ) => _end = end;

    /// <inheritdoc/>
    public override string ToString( ) =>
        _start == _end
            ? _start.ToString( )
            : $"{_start} - {_end}";

    /// <summary>
    /// Formats a collection of <see cref="IntRange"/> instances as a comma-separated string,
    /// ordered by start value.
    /// </summary>
    public static string ToString( IEnumerable<IntRange> ranges )
        => string.Join(
            ", ",
            ranges
                .OrderBy( range => range._start )
                .Select( range => range.ToString( ) ) );

    /// <summary>
    /// Converts a sequence of integers into the minimum set of contiguous <see cref="IntRange"/> instances.
    /// </summary>
    /// <param name="intRange">The integers to group into ranges.</param>
    /// <returns>An enumerable of contiguous ranges.</returns>
    public static IEnumerable<IntRange> GetContiguousRanges( IEnumerable<int> intRange ) {
        List<IntRange> result = [];
        IntRange range = new( );
        long index = 0;
        int[] longRange = [.. intRange.Order( ).Distinct( )];

        for (long i = longRange[0]; i <= longRange.Last( ); i++) {
            if (range._start == DefaultStartValue) {
                if (longRange[index] != i) {
                    i = longRange[index];
                }
                range.SetStart( (int)i );
            }

            long nextIndex = index + 1 < longRange.LongLength
                ? longRange[index + 1]
                : i - 1;

            if (
                longRange[index] == i &&
                nextIndex != i + 1 &&
                range._end == DefaultEndValue
            ) {
                range.SetEnd( (int)i );
                result.Add( range );
                range = new( );
            }
            index++;
        }
        return result;
    }
}
