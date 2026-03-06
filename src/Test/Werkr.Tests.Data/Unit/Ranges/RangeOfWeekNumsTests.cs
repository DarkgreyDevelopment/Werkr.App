using Werkr.Data.Calendar.Enums;
using Werkr.Data.Ranges;

namespace Werkr.Tests.Data.Unit.Ranges;

/// <summary>
/// Contains unit tests for the <see cref="RangeOfWeekNums"/> struct defined in Werkr.Data. Validates <see
/// cref="GetContiguousRanges"/> from <see cref="WeekNumberWithinMonth"/> flags and <see cref="ToString"/> formatting.
/// </summary>
[TestClass]
public class RangeOfWeekNumsTests {

    #region GetContiguousRanges

    /// <summary>
    /// Verifies that a single week flag produces a single range with the same start and end.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_FirstOnly_ReturnsSingleRange( ) {
        List<RangeOfWeekNums> ranges = [.. RangeOfWeekNums.GetContiguousRanges( WeekNumberWithinMonth.First )];

        Assert.HasCount(
            1,
            ranges
        );
        Assert.AreEqual(
            WeekNumberWithinMonth.First,
            ranges[0].Start
        );
        Assert.AreEqual(
            WeekNumberWithinMonth.First,
            ranges[0].End
        );
    }

    /// <summary>
    /// Verifies that three contiguous weeks (First-Third) are collapsed into a single range.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_FirstThroughThird_ReturnsSingleRange( ) {
        WeekNumberWithinMonth weeks = WeekNumberWithinMonth.First | WeekNumberWithinMonth.Second
                                    | WeekNumberWithinMonth.Third;
        List<RangeOfWeekNums> ranges = [.. RangeOfWeekNums.GetContiguousRanges( weeks )];

        Assert.HasCount(
            1,
            ranges
        );
        Assert.AreEqual(
            WeekNumberWithinMonth.First,
            ranges[0].Start
        );
        Assert.AreEqual(
            WeekNumberWithinMonth.Third,
            ranges[0].End
        );
    }

    /// <summary>
    /// Verifies that two non-contiguous weeks (First and Fifth) produce two separate ranges.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_FirstAndFifth_ReturnsTwoRanges( ) {
        WeekNumberWithinMonth weeks = WeekNumberWithinMonth.First | WeekNumberWithinMonth.Fifth;
        List<RangeOfWeekNums> ranges = [.. RangeOfWeekNums.GetContiguousRanges( weeks )];

        Assert.HasCount(
            2,
            ranges
        );
    }

    /// <summary>
    /// Verifies that all six week flags produce a single contiguous range.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_AllSixWeeks_ReturnsSingleRange( ) {
        WeekNumberWithinMonth all = WeekNumberWithinMonth.First | WeekNumberWithinMonth.Second
                                  | WeekNumberWithinMonth.Third | WeekNumberWithinMonth.Fourth
                                  | WeekNumberWithinMonth.Fifth | WeekNumberWithinMonth.Sixth;
        List<RangeOfWeekNums> ranges = [.. RangeOfWeekNums.GetContiguousRanges( all )];

        Assert.HasCount(
            1,
            ranges
        );
    }

    #endregion GetContiguousRanges

    #region ToString

    /// <summary>
    /// Verifies that abbreviated <see cref="ToString"/> returns the numeric value for a single week.
    /// </summary>
    [TestMethod]
    public void ToString_SingleWeek_Abbreviated_ReturnsNumber( ) {
        RangeOfWeekNums range = new( );
        range.SetStart( (int)WeekNumberWithinMonth.Third );
        range.SetEnd( (int)WeekNumberWithinMonth.Third );
        string result = range.ToString( abbreviated: true );
        Assert.AreEqual(
            "3",
            result
        );
    }

    /// <summary>
    /// Verifies that non-abbreviated <see cref="ToString"/> returns the enum name for a single week.
    /// </summary>
    [TestMethod]
    public void ToString_SingleWeek_FullName_ReturnsEnumName( ) {
        RangeOfWeekNums range = new( );
        range.SetStart( (int)WeekNumberWithinMonth.Second );
        range.SetEnd( (int)WeekNumberWithinMonth.Second );
        string result = range.ToString( abbreviated: false );
        Assert.AreEqual(
            "Second",
            result
        );
    }

    /// <summary>
    /// Verifies that abbreviated <see cref="ToString"/> returns a dash-separated range for contiguous weeks.
    /// </summary>
    [TestMethod]
    public void ToString_Range_Abbreviated_ReturnsDashSeparated( ) {
        RangeOfWeekNums range = new( );
        range.SetStart( (int)WeekNumberWithinMonth.First );
        range.SetEnd( (int)WeekNumberWithinMonth.Fourth );
        string result = range.ToString( abbreviated: true );
        Assert.AreEqual(
            "1 - 4",
            result
        );
    }

    #endregion ToString
}
