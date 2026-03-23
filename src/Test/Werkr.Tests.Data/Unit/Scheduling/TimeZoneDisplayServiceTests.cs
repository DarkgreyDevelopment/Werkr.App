using Werkr.Common.Scheduling;

namespace Werkr.Tests.Data.Unit.Scheduling;

/// <summary>
/// Unit tests for <see cref="TimeZoneDisplayService"/>.
/// </summary>
[TestClass]
public class TimeZoneDisplayServiceTests {

    /// <summary>
    /// UTC should always return "UTC" regardless of instant.
    /// </summary>
    [TestMethod]
    public void GetAbbreviation_Utc_ReturnsUtc( ) {
        string abbrev = TimeZoneDisplayService.GetAbbreviation(
            TimeZoneInfo.Utc,
            new DateTime( 2025, 7, 15, 12, 0, 0, DateTimeKind.Utc )
        );
        Assert.AreEqual( "UTC", abbrev );
    }

    /// <summary>
    /// US Eastern in July (DST) should return "EDT".
    /// </summary>
    [TestMethod]
    public void GetAbbreviation_EasternInJuly_ReturnsEdt( ) {
        TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById( "America/New_York" );
        // July 15 is during Eastern Daylight Time
        DateTime july = new( 2025, 7, 15, 12, 0, 0 );
        string abbrev = TimeZoneDisplayService.GetAbbreviation( eastern, july );
        Assert.AreEqual( "EDT", abbrev );
    }

    /// <summary>
    /// US Eastern in January (standard) should return "EST".
    /// </summary>
    [TestMethod]
    public void GetAbbreviation_EasternInJanuary_ReturnsEst( ) {
        TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById( "America/New_York" );
        // January 15 is during Eastern Standard Time
        DateTime january = new( 2025, 1, 15, 12, 0, 0 );
        string abbrev = TimeZoneDisplayService.GetAbbreviation( eastern, january );
        Assert.AreEqual( "EST", abbrev );
    }

    /// <summary>
    /// A fixed-offset timezone should return a GMT label, not a CLDR abbreviation.
    /// </summary>
    [TestMethod]
    public void GetAbbreviation_FixedOffset_ReturnsGmtLabel( ) {
        TimeZoneInfo tz = TimeZoneInfo.CreateCustomTimeZone(
            "UTC+5:30", new TimeSpan( 5, 30, 0 ), "UTC+5:30", "UTC+5:30" );
        string abbrev = TimeZoneDisplayService.GetAbbreviation(
            tz, DateTime.UtcNow );
        Assert.AreEqual( "GMT+5:30", abbrev );
    }

    /// <summary>
    /// GetFixedOffsetLabel with -7 hours should return "GMT-7".
    /// </summary>
    [TestMethod]
    public void GetFixedOffsetLabel_NegativeSeven_ReturnsGmtMinus7( ) {
        string label = TimeZoneDisplayService.GetFixedOffsetLabel( TimeSpan.FromHours( -7 ) );
        Assert.AreEqual( "GMT-7", label );
    }

    /// <summary>
    /// GetFixedOffsetLabel with 5.5 hours should return "GMT+5:30".
    /// </summary>
    [TestMethod]
    public void GetFixedOffsetLabel_FiveAndHalf_ReturnsGmtPlus530( ) {
        string label = TimeZoneDisplayService.GetFixedOffsetLabel( TimeSpan.FromHours( 5.5 ) );
        Assert.AreEqual( "GMT+5:30", label );
    }

    /// <summary>
    /// GetFixedOffsetLabel with zero should return "GMT+0".
    /// </summary>
    [TestMethod]
    public void GetFixedOffsetLabel_Zero_ReturnsGmtPlusZero( ) {
        string label = TimeZoneDisplayService.GetFixedOffsetLabel( TimeSpan.Zero );
        Assert.AreEqual( "GMT+0", label );
    }

    /// <summary>
    /// GetFixedOffsetListItems should cover UTC-12:00 through UTC+14:00 including :30 and :45 entries.
    /// </summary>
    [TestMethod]
    public void GetFixedOffsetListItems_ContainsExpectedRange( ) {
        IReadOnlyList<FixedOffsetListItem> items = TimeZoneDisplayService.GetFixedOffsetListItems( );

        // Should contain UTC-12 as the minimum
        Assert.IsNotNull( items.FirstOrDefault( i => i.Offset == TimeSpan.FromHours( -12 ) ),
            "Should contain UTC-12" );

        // Should contain UTC+14 as the maximum
        Assert.IsNotNull( items.FirstOrDefault( i => i.Offset == TimeSpan.FromHours( 14 ) ),
            "Should contain UTC+14" );

        // Should contain half-hour offsets like UTC+5:30 (India)
        Assert.IsNotNull( items.FirstOrDefault( i => i.Offset == new TimeSpan( 5, 30, 0 ) ),
            "Should contain UTC+5:30" );

        // Should contain quarter-hour offsets like UTC+5:45 (Nepal)
        Assert.IsNotNull( items.FirstOrDefault( i => i.Offset == new TimeSpan( 5, 45, 0 ) ),
            "Should contain UTC+5:45" );

        // Should contain UTC+12:45 (Chatham Islands)
        Assert.IsNotNull( items.FirstOrDefault( i => i.Offset == new TimeSpan( 12, 45, 0 ) ),
            "Should contain UTC+12:45" );

        // Items should be sorted by offset
        for (int i = 1; i < items.Count; i++) {
            Assert.IsGreaterThanOrEqualTo( items[i - 1].Offset, items[i].Offset,
                $"Items should be sorted: {items[i - 1].Id} before {items[i].Id}" );
        }
    }

    /// <summary>
    /// GetTimeZoneListItems should return a non-empty list.
    /// </summary>
    [TestMethod]
    public void GetTimeZoneListItems_ReturnsNonEmptyList( ) {
        IReadOnlyList<TimeZoneListItem> items = TimeZoneDisplayService.GetTimeZoneListItems( );
        Assert.IsNotEmpty( items );
    }
}
