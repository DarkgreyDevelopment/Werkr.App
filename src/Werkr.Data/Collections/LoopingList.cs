using System.Collections;

namespace Werkr.Data.Collections;

/// <summary>
/// Represents a looping list that continuously iterates through its elements.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="LoopingList{T}"/> provides continuous iteration over its elements,
/// wrapping around from the end to the beginning. It implements <see cref="IList{T}"/>,
/// <see cref="ICollection{T}"/>, and <see cref="IEnumerable{T}"/>.
/// </para>
/// <para>
/// When used in a <see langword="foreach"/> loop, it will <b>not</b> automatically break -
/// consumers must manually break to exit the loop.
/// </para>
/// <list type="bullet">
///     <item>
///         <term>Continuous iteration</term>
///         <description>Navigates through elements indefinitely.</description>
///     </item>
///     <item>
///         <term>Starting element</term>
///         <description>Optionally specify the starting element for the loop.</description>
///     </item>
///     <item>
///         <term>Mutable</term>
///         <description>Allows adding, removing, and modifying elements like a regular list.</description>
///     </item>
/// </list>
/// </remarks>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public class LoopingList<T> : IEnumerable<T>, ICollection<T>, IList<T> {

    /// <summary>
    /// Initializes an empty looping list.
    /// </summary>
    public LoopingList( ) {
        _items = [];
    }

    /// <summary>
    /// Initializes a looping list with elements from a provided collection.
    /// </summary>
    /// <param name="items">The collection of elements to add to the list.</param>
    public LoopingList( IEnumerable<T> items ) {
        _items = [.. items];
    }

    /// <summary>
    /// Initializes a looping list with elements from a collection and sets the starting element.
    /// </summary>
    /// <param name="items">The collection of elements to add to the list.</param>
    /// <param name="startingElement">The element to start the iteration from.</param>
    public LoopingList( IEnumerable<T> items, T startingElement ) {
        _items = [.. items];
        if (startingElement is not null && _items.Contains( startingElement )) {
            CurrentIndex = _items.IndexOf( startingElement );
        }
    }

    /// <summary>The internal backing list.</summary>
    private readonly List<T> _items;

    /// <summary>The current iteration index within the list.</summary>
    public int CurrentIndex { get; set; }

    #region ICollection<T>

    /// <inheritdoc/>
    public int Count => _items.Count;

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public void Add( T item ) => _items.Add( item );

    /// <inheritdoc/>
    public void Clear( ) => _items.Clear( );

    /// <inheritdoc/>
    public bool Contains( T item ) => _items.Contains( item );

    /// <inheritdoc/>
    public void CopyTo( T[] array, int arrayIndex ) => _items.CopyTo( array, arrayIndex );

    /// <inheritdoc/>
    public bool Remove( T item ) => _items.Remove( item );

    #endregion ICollection<T>

    #region IList<T>

    /// <inheritdoc/>
    /// <remarks>Indexes wrap around using modulo arithmetic. Negative indexes are not supported.</remarks>
    public T this[int index] { get => _items[index % _items.Count]; set => _items[index % _items.Count] = value; }

    /// <inheritdoc/>
    public int IndexOf( T item ) => _items.IndexOf( item );

    /// <inheritdoc/>
    public void Insert( int index, T item ) => _items.Insert( index, item );

    /// <inheritdoc/>
    public void RemoveAt( int index ) => _items.RemoveAt( index );

    #endregion IList<T>

    #region IEnumerable<T>

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator( ) {
        while (true) {
            yield return _items[CurrentIndex];
            CurrentIndex = (CurrentIndex + 1) % _items.Count;
        }
    }

    IEnumerator IEnumerable.GetEnumerator( ) => GetEnumerator( );

    #endregion IEnumerable<T>
}
