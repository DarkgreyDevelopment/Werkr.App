using Werkr.Data.Collections;

namespace Werkr.Tests.Data.Unit.Collections;

[TestClass]
public class LoopingListTests {

    [TestMethod]
    public void Constructor_PopulatesListWithCorrectItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.AreEqual( 5, loopingList.Count );
    }

    [TestMethod]
    public void Indexer_ReturnsCorrectItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.AreEqual( 1, loopingList[0] );
        Assert.AreEqual( 2, loopingList[1] );
        Assert.AreEqual( 3, loopingList[2] );
        Assert.AreEqual( 4, loopingList[3] );
        Assert.AreEqual( 5, loopingList[4] );
    }

    [TestMethod]
    public void Indexer_WrapsAroundCorrectly( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.AreEqual( 1, loopingList[5] );
        Assert.AreEqual( 2, loopingList[6] );
        Assert.AreEqual( 3, loopingList[7] );
        Assert.AreEqual( 4, loopingList[8] );
        Assert.AreEqual( 5, loopingList[9] );
    }

    [TestMethod]
    public void Indexer_NegativeIndex_ThrowsArgumentOutOfRangeException( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>( ( ) => _ = loopingList[-1] );
    }

    [TestMethod]
    public void Add_IncreasesCountByOne( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items,  6];
        Assert.AreEqual( 6, loopingList.Count );
        Assert.AreEqual( 6, loopingList[5] );
    }

    [TestMethod]
    public void Remove_DecreasesCountByOne( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        _ = loopingList.Remove( 3 );
        Assert.AreEqual( 4, loopingList.Count );
        Assert.AreEqual( 4, loopingList[2] );
    }

    [TestMethod]
    public void Contains_ReturnsTrueForExistingItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.IsTrue( loopingList.Contains( 3 ) );
    }

    [TestMethod]
    public void Contains_ReturnsFalseForNonExistingItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.IsFalse( loopingList.Contains( 6 ) );
    }

    [TestMethod]
    public void IndexOf_ReturnsCorrectIndexForExistingItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.AreEqual( 2, loopingList.IndexOf( 3 ) );
    }

    [TestMethod]
    public void IndexOf_ReturnsNegativeOneForNonExistingItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        Assert.AreEqual( -1, loopingList.IndexOf( 6 ) );
    }

    [TestMethod]
    public void Insert_InsertsItemAtCorrectIndex( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        loopingList.Insert( 2, 6 );
        Assert.AreEqual( 6, loopingList.Count );
        Assert.AreEqual( 6, loopingList[2] );
    }

    [TestMethod]
    public void Clear_RemovesAllItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        loopingList.Clear( );
        Assert.AreEqual( 0, loopingList.Count );
    }

    [TestMethod]
    public void Enumerator_IteratesOverAllItems( ) {
        List<int> items = [1, 2, 3, 4, 5];
        LoopingList<int> loopingList = [.. items];
        int count = 0;
        foreach (int item in loopingList) {
            Assert.AreEqual( items[count % items.Count], item );
            count++;
            if (count >= 10) {
                break; // Two full cycles
            }
        }
        Assert.AreEqual( 10, count );
    }

}
