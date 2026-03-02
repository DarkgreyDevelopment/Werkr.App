using Werkr.Data.Calendar.Enums;
using Werkr.Data.Ranges;

namespace Werkr.Tests.Data.Unit.Ranges;

[TestClass]
public class RangeOfWeekNumsTests {

    #region GetContiguousRanges

    [TestMethod]
    public void GetContiguousRanges_FirstOnly_ReturnsSingleRange( ) {
        List<RangeOfWeekNums> ranges = [.. RangeOfWeekNums
            .GetContiguousRanges( WeekNumberWithinMonth.First )];

        Assert.HasCount( 1, ranges );
        Assert.AreEqual( WeekNumberWithinMonth.First, ranges[0].Start );
        Assert.AreEqual( WeekNumberWithinMonth.First, ranges[0].End );
    }

    [TestMethod]
    public void GetContiguousRanges_FirstThroughThird_ReturnsSingleRange( ) {
        WeekNumberWithinMonth weeks = WeekNumberWithinMonth.First | WeekNumberWithinMonth.Second
                                    | WeekNumberWithinMonth.Third;
        List<RangeOfWeekNums> ranges = [.. RangeOfWeekNums
            .GetContiguousRanges( weeks )];

        Assert.HasCount( 1, ranges );
        Assert.AreEqual( WeekNumberWithinMonth.First, ranges[0].Start );
        Assert.AreEqual( WeekNumberWithinMonth.Third, ranges[0].End );
    }

    [TestMethod]
    public void GetContiguousRanges_FirstAndFifth_ReturnsTwoRanges( ) {
        WeekNumberWithinMonth weeks = WeekNumberWithinMonth.First | WeekNumberWithinMonth.Fifth;
        List<RangeOfWeekNums> ranges = [.. RangeOfWeekNums
            .GetContiguousRanges( weeks )];

        Assert.HasCount( 2, ranges );
    }

    [TestMethod]
    public void GetContiguousRanges_AllSixWeeks_ReturnsSingleRange( ) {
        WeekNumberWithinMonth all = WeekNumberWithinMonth.First | WeekNumberWithinMonth.Second
                                  | WeekNumberWithinMonth.Third | WeekNumberWithinMonth.Fourth
                                  | WeekNumberWithinMonth.Fifth | WeekNumberWithinMonth.Sixth;
        List<RangeOfWeekNums> ranges = [.. RangeOfWeekNums
            .GetContiguousRanges( all )];

        Assert.HasCount( 1, ranges );
    }

    #endregion GetContiguousRanges

    #region ToString

    [TestMethod]
    public void ToString_SingleWeek_Abbreviated_ReturnsNumber( ) {
        RangeOfWeekNums range = new( );
        range.SetStart( (int)WeekNumberWithinMonth.Third );
        range.SetEnd( (int)WeekNumberWithinMonth.Third );
        string result = range.ToString( abbreviated: true );
        Assert.AreEqual( "3", result );
    }

    [TestMethod]
    public void ToString_SingleWeek_FullName_ReturnsEnumName( ) {
        RangeOfWeekNums range = new( );
        range.SetStart( (int)WeekNumberWithinMonth.Second );
        range.SetEnd( (int)WeekNumberWithinMonth.Second );
        string result = range.ToString( abbreviated: false );
        Assert.AreEqual( "Second", result );
    }

    [TestMethod]
    public void ToString_Range_Abbreviated_ReturnsDashSeparated( ) {
        RangeOfWeekNums range = new( );
        range.SetStart( (int)WeekNumberWithinMonth.First );
        range.SetEnd( (int)WeekNumberWithinMonth.Fourth );
        string result = range.ToString( abbreviated: true );
        Assert.AreEqual( "1 - 4", result );
    }

    #endregion ToString
}
