using Werkr.Data;

namespace Werkr.Tests.Data.Unit.Scheduling;

/// <summary>
/// Unit tests for the <see cref="TimeZoneResolver"/> static helper.
/// </summary>
[TestClass]
public class TimeZoneResolverTests {

    /// <summary>
    /// A named IANA/Windows timezone ID should resolve via the system.
    /// </summary>
    [TestMethod]
    public void FindOrCreate_NamedTimezone_ReturnsSystemTimeZone( ) {
        // Use a timezone that exists on all platforms
        TimeZoneInfo tz = TimeZoneResolver.FindOrCreate( "UTC" );
        Assert.AreEqual( TimeSpan.Zero, tz.BaseUtcOffset );
    }

    /// <summary>
    /// A fixed-offset ID like UTC+5:30 should return a TimeZoneInfo with the correct offset and no DST rules.
    /// </summary>
    [TestMethod]
    public void FindOrCreate_FixedOffsetWithMinutes_ReturnsCorrectOffset( ) {
        TimeZoneInfo tz = TimeZoneResolver.FindOrCreate( "UTC+5:30" );
        Assert.AreEqual( new TimeSpan( 5, 30, 0 ), tz.BaseUtcOffset );
        Assert.IsEmpty( tz.GetAdjustmentRules( ) );
    }

    /// <summary>
    /// A fixed-offset ID with whole hours should work.
    /// </summary>
    [TestMethod]
    public void FindOrCreate_FixedOffsetWholeHours_ReturnsCorrectOffset( ) {
        TimeZoneInfo tz = TimeZoneResolver.FindOrCreate( "UTC-7" );
        Assert.AreEqual( new TimeSpan( -7, 0, 0 ), tz.BaseUtcOffset );
        Assert.IsEmpty( tz.GetAdjustmentRules( ) );
    }

    /// <summary>
    /// A negative fixed-offset ID with minutes should produce a negative total offset.
    /// </summary>
    [TestMethod]
    public void FindOrCreate_NegativeOffsetWithMinutes_ReturnsCorrectOffset( ) {
        TimeZoneInfo tz = TimeZoneResolver.FindOrCreate( "UTC-9:30" );
        Assert.AreEqual( new TimeSpan( -9, -30, 0 ), tz.BaseUtcOffset );
    }

    /// <summary>
    /// A positive two-digit offset should work.
    /// </summary>
    [TestMethod]
    public void FindOrCreate_PositiveTwoDigitOffset_ReturnsCorrectOffset( ) {
        TimeZoneInfo tz = TimeZoneResolver.FindOrCreate( "UTC+12" );
        Assert.AreEqual( new TimeSpan( 12, 0, 0 ), tz.BaseUtcOffset );
    }

    /// <summary>
    /// An invalid ID should throw TimeZoneNotFoundException.
    /// </summary>
    [TestMethod]
    public void FindOrCreate_GarbageId_ThrowsTimeZoneNotFoundException( ) {
        Assert.ThrowsExactly<TimeZoneNotFoundException>(
            ( ) => TimeZoneResolver.FindOrCreate( "garbage" ) );
    }

    /// <summary>
    /// An ID that looks like a fixed offset but has invalid format should throw.
    /// </summary>
    [TestMethod]
    public void FindOrCreate_InvalidFormat_ThrowsTimeZoneNotFoundException( ) {
        Assert.ThrowsExactly<TimeZoneNotFoundException>(
            ( ) => TimeZoneResolver.FindOrCreate( "UTC+abc" ) );
    }
}
