using Werkr.Data.Collections;

namespace Werkr.Tests.Data.Unit.Collections;

/// <summary>
/// Contains unit tests for the <see cref="LoopingList{T}"/> collection type defined in Werkr.Data. Validates
/// constructor behavior, indexer wrapping, mutation operations (add, remove, insert, clear), search operations
/// (contains, indexOf), and the infinite enumerator behavior.
/// </summary>
[TestClass]
public class LoopingListTests {

    /// <summary>
    /// Verifies that the constructor correctly populates the list with the specified items and sets the count.
    /// </summary>
    [TestMethod]
    public void Constructor_PopulatesListWithCorrectItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.AreEqual(
            5,
            loopingList.Count
        );
    }

    /// <summary>
    /// Verifies that the indexer returns the correct item for each valid index within the list bounds.
    /// </summary>
    [TestMethod]
    public void Indexer_ReturnsCorrectItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.AreEqual(
            1,
            loopingList[0]
        );
        Assert.AreEqual(
            2,
            loopingList[1]
        );
        Assert.AreEqual(
            3,
            loopingList[2]
        );
        Assert.AreEqual(
            4,
            loopingList[3]
        );
        Assert.AreEqual(
            5,
            loopingList[4]
        );
    }

    /// <summary>
    /// Verifies that the indexer wraps around to the beginning when accessing indices beyond the list length.
    /// </summary>
    [TestMethod]
    public void Indexer_WrapsAroundCorrectly( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.AreEqual(
            1,
            loopingList[5]
        );
        Assert.AreEqual(
            2,
            loopingList[6]
        );
        Assert.AreEqual(
            3,
            loopingList[7]
        );
        Assert.AreEqual(
            4,
            loopingList[8]
        );
        Assert.AreEqual(
            5,
            loopingList[9]
        );
    }

    /// <summary>
    /// Verifies that accessing a negative index throws an <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    [TestMethod]
    public void Indexer_NegativeIndex_ThrowsArgumentOutOfRangeException( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>( ( ) => _ = loopingList[-1] );
    }

    /// <summary>
    /// Verifies that adding an item increases the count by one and the new item is accessible.
    /// </summary>
    [TestMethod]
    public void Add_IncreasesCountByOne( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items,  6];
        Assert.AreEqual(
            6,
            loopingList.Count
        );
        Assert.AreEqual(
            6,
            loopingList[5]
        );
    }

    /// <summary>
    /// Verifies that removing an existing item decreases the count and shifts subsequent items.
    /// </summary>
    [TestMethod]
    public void Remove_DecreasesCountByOne( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        _ = loopingList.Remove( 3 );
        Assert.AreEqual(
            4,
            loopingList.Count
        );
        Assert.AreEqual(
            4,
            loopingList[2]
        );
    }

    /// <summary>
    /// Verifies that <see cref="Contains"/> returns <see langword="true"/> when the item exists in the list.
    /// </summary>
    [TestMethod]
    public void Contains_ReturnsTrueForExistingItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.IsTrue( loopingList.Contains( 3 ) );
    }

    /// <summary>
    /// Verifies that <see cref="Contains"/> returns <see langword="false"/> when the item does not exist in the list.
    /// </summary>
    [TestMethod]
    public void Contains_ReturnsFalseForNonExistingItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.IsFalse( loopingList.Contains( 6 ) );
    }

    /// <summary>
    /// Verifies that <see cref="IndexOf"/> returns the correct zero-based index for an existing item.
    /// </summary>
    [TestMethod]
    public void IndexOf_ReturnsCorrectIndexForExistingItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.AreEqual(
            2,
            loopingList.IndexOf( 3 )
        );
    }

    /// <summary>
    /// Verifies that <see cref="IndexOf"/> returns <c>-1</c> when the item does not exist in the list.
    /// </summary>
    [TestMethod]
    public void IndexOf_ReturnsNegativeOneForNonExistingItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.AreEqual(
            -1,
            loopingList.IndexOf( 6 )
        );
    }

    /// <summary>
    /// Verifies that inserting an item at a specific index places it correctly and increases the count.
    /// </summary>
    [TestMethod]
    public void Insert_InsertsItemAtCorrectIndex( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        loopingList.Insert(
            2,
            6
        );
        Assert.AreEqual(
            6,
            loopingList.Count
        );
        Assert.AreEqual(
            6,
            loopingList[2]
        );
    }

    /// <summary>
    /// Verifies that clearing the list removes all items and sets the count to zero.
    /// </summary>
    [TestMethod]
    public void Clear_RemovesAllItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        loopingList.Clear( );
        Assert.AreEqual(
            0,
            loopingList.Count
        );
    }

    /// <summary>
    /// Verifies that the enumerator iterates infinitely, wrapping around to the beginning after reaching the end.
    /// Validates that at least 10 items can be enumerated from a 5-element list with correct wrap-around values.
    /// </summary>
    [TestMethod]
    public void Enumerator_IteratesOverAllItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        int count = 0;
        foreach (int item in loopingList) {
            Assert.AreEqual(
                items[count % items.Count],
                item
            );
            count++;
            if (count >= 10) {
                break; // Two full cycles
            }
        }
        Assert.AreEqual(
            10,
            count
        );
    }

}
