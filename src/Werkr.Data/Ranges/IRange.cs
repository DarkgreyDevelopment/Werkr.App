namespace Werkr.Data.Ranges;

/// <summary>
/// Defines a range with a start and end value.
/// </summary>
/// <typeparam name="T">The value type of the range bounds.</typeparam>
public interface IRange<T> where T : struct {

    /// <summary>The start of the range.</summary>
    T Start { get; }

    /// <summary>The end of the range.</summary>
    T End { get; }
}
