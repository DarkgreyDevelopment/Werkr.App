using Werkr.Data.Calendar.Enums;
using Werkr.Data.Ranges;

namespace Werkr.Tests.Data.Unit.Ranges;

/// <summary>
/// Contains unit tests for the <see cref="RangeOfMonths"/> struct defined in Werkr.Data. Validates <see
/// cref="GetContiguousRanges"/> from <see cref="MonthsOfYear"/> flags and <see cref="ToString"/> formatting.
/// </summary>
[TestClass]
public class RangeOfMonthsTests {

    #region GetContiguousRanges

    /// <summary>
    /// Verifies that a single month flag produces a single range with the same start and end.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_SingleMonth_ReturnsSingleRange( ) {
        List<RangeOfMonths> ranges = [.. RangeOfMonths .GetContiguousRanges( MonthsOfYear.March )];

        Assert.HasCount(
            1,
            ranges
        );
        Assert.AreEqual(
            Month.March,
            ranges[0].Start
        );
        Assert.AreEqual(
            Month.March,
            ranges[0].End
        );
    }

    /// <summary>
    /// Verifies that three contiguous months (Q1) are collapsed into a single range.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_FirstQuarter_ReturnsSingleRange( ) {
        MonthsOfYear q1 = MonthsOfYear.January | MonthsOfYear.February | MonthsOfYear.March;
        List<RangeOfMonths> ranges = [.. RangeOfMonths .GetContiguousRanges( q1 )];

        Assert.HasCount(
            1,
            ranges
        );
        Assert.AreEqual(
            Month.January,
            ranges[0].Start
        );
        Assert.AreEqual(
            Month.March,
            ranges[0].End
        );
    }

    /// <summary>
    /// Verifies that four quarterly months produce four separate ranges.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_Quarterly_ReturnsFourRanges( ) {
        MonthsOfYear quarterly = MonthsOfYear.January | MonthsOfYear.April
                               | MonthsOfYear.July | MonthsOfYear.October;
        List<RangeOfMonths> ranges = [.. RangeOfMonths .GetContiguousRanges( quarterly )];

        Assert.HasCount(
            4,
            ranges
        );
    }

    /// <summary>
    /// Verifies that all twelve months produce a single range spanning January through December.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_AllMonths_ReturnsSingleRange( ) {
        MonthsOfYear all = MonthsOfYear.January | MonthsOfYear.February | MonthsOfYear.March
                         | MonthsOfYear.April | MonthsOfYear.May | MonthsOfYear.June
                         | MonthsOfYear.July | MonthsOfYear.August | MonthsOfYear.September
                         | MonthsOfYear.October | MonthsOfYear.November | MonthsOfYear.December;
        List<RangeOfMonths> ranges = [.. RangeOfMonths .GetContiguousRanges( all )];

        Assert.HasCount(
            1,
            ranges
        );
        Assert.AreEqual(
            Month.January,
            ranges[0].Start
        );
        Assert.AreEqual(
            Month.December,
            ranges[0].End
        );
    }

    /// <summary>
    /// Verifies that three non-contiguous months produce three separate ranges.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_JanMaySep_ReturnsThreeRanges( ) {
        MonthsOfYear months = MonthsOfYear.January | MonthsOfYear.May | MonthsOfYear.September;
        List<RangeOfMonths> ranges = [.. RangeOfMonths .GetContiguousRanges( months )];

        Assert.HasCount(
            3,
            ranges
        );
    }

    #endregion GetContiguousRanges

    #region ToString

    /// <summary>
    /// Verifies that abbreviated <see cref="ToString"/> returns the short month name for a single month.
    /// </summary>
    [TestMethod]
    public void ToString_SingleMonth_ReturnsAbbreviatedName( ) {
        RangeOfMonths range = new( );
        range.SetStart( 6 );
        range.SetEnd( 6 );
        string result = range.ToString( abbreviated: true );
        Assert.AreEqual(
            "Jun",
            result
        );
    }

    /// <summary>
    /// Verifies that abbreviated <see cref="ToString"/> returns a dash-separated range for contiguous months.
    /// </summary>
    [TestMethod]
    public void ToString_Range_ReturnsDashSeparated( ) {
        RangeOfMonths range = new( );
        range.SetStart( 1 );
        range.SetEnd( 3 );
        string result = range.ToString( abbreviated: true );
        Assert.AreEqual(
            "Jan - Mar",
            result
        );
    }

    /// <summary>
    /// Verifies that non-abbreviated <see cref="ToString"/> returns the full month name.
    /// </summary>
    [TestMethod]
    public void ToString_FullNames_ReturnsFullNames( ) {
        RangeOfMonths range = new( );
        range.SetStart( 12 );
        range.SetEnd( 12 );
        string result = range.ToString( abbreviated: false );
        Assert.AreEqual(
            "December",
            result
        );
    }

    #endregion ToString
}
