using Werkr.Data.Ranges;

namespace Werkr.Tests.Data.Unit.Ranges;

[TestClass]
public class IntRangeTests {

    #region Constructor & Properties

    [TestMethod]
    public void DefaultConstructor_StartReturnsMinValue( ) {
        IntRange range = new( );
        Assert.AreEqual( int.MinValue, range.Start );
    }

    [TestMethod]
    public void DefaultConstructor_EndReturnsMaxValue( ) {
        IntRange range = new( );
        Assert.AreEqual( int.MaxValue, range.End );
    }

    [TestMethod]
    public void ExplicitConstructor_SetsStartAndEnd( ) {
        IntRange range = new( 5, 10 );
        Assert.AreEqual( 5, range.Start );
        Assert.AreEqual( 10, range.End );
    }

    [TestMethod]
    public void SetStart_UpdatesStartProperty( ) {
        IntRange range = new( );
        range.SetStart( 42 );
        Assert.AreEqual( 42, range.Start );
    }

    [TestMethod]
    public void SetEnd_UpdatesEndProperty( ) {
        IntRange range = new( );
        range.SetEnd( 99 );
        Assert.AreEqual( 99, range.End );
    }

    #endregion Constructor & Properties

    #region ToString

    [TestMethod]
    public void ToString_SingleValue_ReturnsOneNumber( ) {
        IntRange range = new( 7, 7 );
        Assert.AreEqual( "7", range.ToString( ) );
    }

    [TestMethod]
    public void ToString_Range_ReturnsDashSeparated( ) {
        IntRange range = new( 3, 8 );
        Assert.AreEqual( "3 - 8", range.ToString( ) );
    }

    [TestMethod]
    public void ToString_MultipleRanges_ReturnsCommaSeparated( ) {
        IntRange[] ranges = [new( 1, 3 ), new( 10, 10 ), new( 7, 9 )];
        string result = IntRange.ToString( ranges );
        Assert.AreEqual( "1 - 3, 7 - 9, 10", result );
    }

    #endregion ToString

    #region GetContiguousRanges

    [TestMethod]
    public void GetContiguousRanges_SingleValue_ReturnsSingleRange( ) {
        List<IntRange> list = [.. IntRange.GetContiguousRanges( [5] )];
        Assert.HasCount( 1, list );
        Assert.AreEqual( 5, list[0].Start );
        Assert.AreEqual( 5, list[0].End );
    }

    [TestMethod]
    public void GetContiguousRanges_ContiguousSequence_ReturnsSingleRange( ) {
        List<IntRange> list = [.. IntRange.GetContiguousRanges( [1, 2, 3, 4, 5] )];
        Assert.HasCount( 1, list );
        Assert.AreEqual( 1, list[0].Start );
        Assert.AreEqual( 5, list[0].End );
    }

    [TestMethod]
    public void GetContiguousRanges_TwoGaps_ReturnsThreeRanges( ) {
        List<IntRange> list = [.. IntRange.GetContiguousRanges( [1, 2, 5, 6, 7, 10] )];
        Assert.HasCount( 3, list );
        Assert.AreEqual( 1, list[0].Start );
        Assert.AreEqual( 2, list[0].End );
        Assert.AreEqual( 5, list[1].Start );
        Assert.AreEqual( 7, list[1].End );
        Assert.AreEqual( 10, list[2].Start );
        Assert.AreEqual( 10, list[2].End );
    }

    [TestMethod]
    public void GetContiguousRanges_UnsortedDuplicates_SortsAndDeduplicates( ) {
        List<IntRange> list = [.. IntRange.GetContiguousRanges( [3, 1, 2, 2, 3] )];
        Assert.HasCount( 1, list );
        Assert.AreEqual( 1, list[0].Start );
        Assert.AreEqual( 3, list[0].End );
    }

    [TestMethod]
    public void GetContiguousRanges_AllDisjoint_ReturnsOneRangePerValue( ) {
        List<IntRange> list = [.. IntRange.GetContiguousRanges( [1, 3, 5] )];
        Assert.HasCount( 3, list );
    }

    #endregion GetContiguousRanges
}
