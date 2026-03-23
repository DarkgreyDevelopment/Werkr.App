using Werkr.Data.Calendar.Enums;
using Werkr.Data.Ranges;

namespace Werkr.Tests.Data.Unit.Ranges;

/// <summary>
/// Contains unit tests for the <see cref="RangeOfDays"/> struct defined in Werkr.Data. Validates <see
/// cref="GetContiguousRanges"/> from <see cref="DaysOfWeek"/> flags and <see cref="ToString"/> formatting.
/// </summary>
[TestClass]
public class RangeOfDaysTests {

    #region GetContiguousRanges

    /// <summary>
    /// Verifies that a single day flag produces a single range with the same start and end.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_SingleDay_ReturnsSingleRange( ) {
        List<RangeOfDays> ranges = [.. RangeOfDays.GetContiguousRanges( DaysOfWeek.Wednesday )];

        Assert.HasCount(
            1,
            ranges
        );
        Assert.AreEqual(
            DayOfWeek.Wednesday,
            ranges[0].Start
        );
        Assert.AreEqual(
            DayOfWeek.Wednesday,
            ranges[0].End
        );
    }

    /// <summary>
    /// Verifies that contiguous weekdays (Mon-Fri) are collapsed into a single range.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_ContiguousDays_ReturnsSingleRange( ) {
        DaysOfWeek weekdays = DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday
                            | DaysOfWeek.Thursday | DaysOfWeek.Friday;
        List<RangeOfDays> ranges = [.. RangeOfDays.GetContiguousRanges( weekdays )];

        Assert.HasCount(
            1,
            ranges
        );
        Assert.AreEqual(
            DayOfWeek.Monday,
            ranges[0].Start
        );
        Assert.AreEqual(
            DayOfWeek.Friday,
            ranges[0].End
        );
    }

    /// <summary>
    /// Verifies that three non-contiguous days produce three separate ranges.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_MondayWednesdayFriday_ReturnsThreeRanges( ) {
        DaysOfWeek days = DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday;
        List<RangeOfDays> ranges = [.. RangeOfDays.GetContiguousRanges( days )];

        Assert.HasCount(
            3,
            ranges
        );
    }

    /// <summary>
    /// Verifies that all seven days produce a single range spanning Sunday through Saturday.
    /// </summary>
    [TestMethod]
    public void GetContiguousRanges_AllDays_ReturnsSingleRange( ) {
        DaysOfWeek all = DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday
                       | DaysOfWeek.Thursday | DaysOfWeek.Friday | DaysOfWeek.Saturday
                       | DaysOfWeek.Sunday;
        List<RangeOfDays> ranges = [.. RangeOfDays.GetContiguousRanges( all )];

        Assert.HasCount(
            1,
            ranges
        );
        Assert.AreEqual(
            DayOfWeek.Sunday,
            ranges[0].Start
        );
        Assert.AreEqual(
            DayOfWeek.Saturday,
            ranges[0].End
        );
    }

    #endregion GetContiguousRanges

    #region ToString

    /// <summary>
    /// Verifies that abbreviated <see cref="ToString"/> returns the short day name for a single day.
    /// </summary>
    [TestMethod]
    public void ToString_SingleDay_ReturnsAbbreviatedName( ) {
        RangeOfDays range = new( );
        range.SetStart( (int)DayOfWeek.Monday );
        range.SetEnd( (int)DayOfWeek.Monday );
        string result = range.ToString( abbreviated: true );
        Assert.AreEqual(
            "Mon",
            result
        );
    }

    /// <summary>
    /// Verifies that abbreviated <see cref="ToString"/> returns a dash-separated range for multiple contiguous days.
    /// </summary>
    [TestMethod]
    public void ToString_Range_ReturnsDashSeparated( ) {
        RangeOfDays range = new( );
        range.SetStart( (int)DayOfWeek.Monday );
        range.SetEnd( (int)DayOfWeek.Friday );
        string result = range.ToString( abbreviated: true );
        Assert.AreEqual(
            "Mon - Fri",
            result
        );
    }

    /// <summary>
    /// Verifies that non-abbreviated <see cref="ToString"/> returns the full day name for a single day.
    /// </summary>
    [TestMethod]
    public void ToString_FullNames_ReturnsFullNames( ) {
        RangeOfDays range = new( );
        range.SetStart( (int)DayOfWeek.Monday );
        range.SetEnd( (int)DayOfWeek.Monday );
        string result = range.ToString( abbreviated: false );
        Assert.AreEqual(
            "Monday",
            result
        );
    }

    #endregion ToString
}
