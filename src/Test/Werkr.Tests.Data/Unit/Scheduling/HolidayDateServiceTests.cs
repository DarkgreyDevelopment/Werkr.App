using Werkr.Core.Scheduling;
using Werkr.Data;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Tests.Data.Unit.Scheduling;

[TestClass]
public class HolidayDateServiceTests {
    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private HolidayDateService _service = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _connection = new SqliteConnection( "DataSource=:memory:" );
        _connection.Open( );

        DbContextOptions<SqliteWerkrDbContext> options = new DbContextOptionsBuilder<SqliteWerkrDbContext>( )
            .UseSqlite( _connection )
            .Options;

        _dbContext = new SqliteWerkrDbContext( options );
        _ = _dbContext.Database.EnsureCreated( );

        _service = new HolidayDateService( _dbContext, NullLogger<HolidayDateService>.Instance );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    #region Helpers

    private async Task<HolidayCalendar> SeedCalendarWithRulesAsync( CancellationToken ct ) {
        HolidayCalendar cal = new( ) {
            Id = Guid.NewGuid( ),
            Name = "Test Calendar",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        _ = _dbContext.HolidayCalendars.Add( cal );

        _ = _dbContext.HolidayRules.Add( new HolidayRule {
            HolidayCalendarId = cal.Id,
            Name = "New Year's Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 1,
            Day = 1,
        } );
        _ = _dbContext.HolidayRules.Add( new HolidayRule {
            HolidayCalendarId = cal.Id,
            Name = "Independence Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 7,
            Day = 4,
        } );
        _ = _dbContext.HolidayRules.Add( new HolidayRule {
            HolidayCalendarId = cal.Id,
            Name = "Christmas",
            RuleType = HolidayRuleType.FixedDate,
            Month = 12,
            Day = 25,
        } );
        _ = await _dbContext.SaveChangesAsync( ct );
        return cal;
    }

    private async Task<HolidayCalendar> SeedEmptyCalendarAsync( CancellationToken ct ) {
        HolidayCalendar cal = new( ) {
            Id = Guid.NewGuid( ),
            Name = "Empty Calendar",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        _ = _dbContext.HolidayCalendars.Add( cal );
        _ = await _dbContext.SaveChangesAsync( ct );
        return cal;
    }

    #endregion

    // ── Materialization ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task MaterializeDates_CreatesDatesFromRules( ) {
        CancellationToken ct = TestContext.CancellationTokenSource.Token;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct );

        await _service.MaterializeDatesAsync( cal.Id, 2026, 2026, ct );

        List<HolidayDate> dates = await _dbContext.HolidayDates
            .Where( d => d.HolidayCalendarId == cal.Id )
            .ToListAsync( ct );

        Assert.HasCount( 3, dates );
        Assert.IsTrue( dates.All( d => d.HolidayRuleId != null ) );
    }

    [TestMethod]
    public async Task MaterializeDates_MultiYear_CreatesAll( ) {
        CancellationToken ct = TestContext.CancellationTokenSource.Token;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct );

        await _service.MaterializeDatesAsync( cal.Id, 2025, 2027, ct );

        int count = await _dbContext.HolidayDates
            .CountAsync( d => d.HolidayCalendarId == cal.Id, ct );

        // 3 rules × 3 years = 9
        Assert.AreEqual( 9, count );
    }

    [TestMethod]
    public async Task MaterializeDates_Idempotent_NoDoubleInsert( ) {
        CancellationToken ct = TestContext.CancellationTokenSource.Token;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct );

        await _service.MaterializeDatesAsync( cal.Id, 2026, 2026, ct );
        await _service.MaterializeDatesAsync( cal.Id, 2026, 2026, ct );

        int count = await _dbContext.HolidayDates
            .CountAsync( d => d.HolidayCalendarId == cal.Id, ct );

        Assert.AreEqual( 3, count );
    }

    [TestMethod]
    public async Task MaterializeDates_EmptyCalendar_NoExceptions( ) {
        CancellationToken ct = TestContext.CancellationTokenSource.Token;
        HolidayCalendar cal = await SeedEmptyCalendarAsync( ct );

        await _service.MaterializeDatesAsync( cal.Id, 2026, 2026, ct );

        int count = await _dbContext.HolidayDates
            .CountAsync( d => d.HolidayCalendarId == cal.Id, ct );

        Assert.AreEqual( 0, count );
    }

    [TestMethod]
    public async Task MaterializeDates_NonexistentCalendar_NoExceptions( ) {
        CancellationToken ct = TestContext.CancellationTokenSource.Token;
        await _service.MaterializeDatesAsync( Guid.NewGuid( ), 2026, 2026, ct );
        // Should not throw
    }

    // ── Invalidation ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task InvalidateCache_RemovesRuleGenerated_PreservesManual( ) {
        CancellationToken ct = TestContext.CancellationTokenSource.Token;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct );

        // Materialize rule-generated dates
        await _service.MaterializeDatesAsync( cal.Id, 2026, 2026, ct );

        // Add a manual date
        _ = _dbContext.HolidayDates.Add( new HolidayDate {
            HolidayCalendarId = cal.Id,
            Date = new DateOnly( 2026, 3, 15 ),
            Name = "Manual Holiday",
            Year = 2026,
            HolidayRuleId = null,
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        int totalBefore = await _dbContext.HolidayDates.CountAsync( d => d.HolidayCalendarId == cal.Id, ct );
        Assert.AreEqual( 4, totalBefore ); // 3 rule + 1 manual

        await _service.InvalidateCacheAsync( cal.Id, ct );

        List<HolidayDate> remaining = await _dbContext.HolidayDates
            .Where( d => d.HolidayCalendarId == cal.Id )
            .ToListAsync( ct );

        Assert.HasCount( 1, remaining );
        Assert.IsNull( remaining[0].HolidayRuleId ); // manual entry preserved
        Assert.AreEqual( "Manual Holiday", remaining[0].Name );
    }

    // ── Auto-Materialization ───────────────────────────────────────────────────

    [TestMethod]
    public async Task GetDatesForRange_AutoMaterializesWhenMissing( ) {
        CancellationToken ct = TestContext.CancellationTokenSource.Token;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct );

        // No dates materialized yet
        IReadOnlyList<HolidayDate> dates = await _service.GetDatesForRangeAsync(
            cal.Id, new DateOnly( 2026, 1, 1 ), new DateOnly( 2026, 12, 31 ), ct );

        Assert.HasCount( 3, dates );
    }

    [TestMethod]
    public async Task GetDatesForRange_IncludesManualDates( ) {
        CancellationToken ct = TestContext.CancellationTokenSource.Token;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct );

        // Add a manual date
        _ = _dbContext.HolidayDates.Add( new HolidayDate {
            HolidayCalendarId = cal.Id,
            Date = new DateOnly( 2026, 6, 19 ),
            Name = "Juneteenth (manual)",
            Year = 2026,
            HolidayRuleId = null,
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        IReadOnlyList<HolidayDate> dates = await _service.GetDatesForRangeAsync(
            cal.Id, new DateOnly( 2026, 1, 1 ), new DateOnly( 2026, 12, 31 ), ct );

        // 3 from rules + 1 manual = 4
        Assert.HasCount( 4, dates );
    }

    // ── Merge H17 ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task MaterializeDates_MergesOntoManualEntry( ) {
        CancellationToken ct = TestContext.CancellationTokenSource.Token;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct );

        // Pre-insert a manual entry matching a rule's output date
        _ = _dbContext.HolidayDates.Add( new HolidayDate {
            HolidayCalendarId = cal.Id,
            Date = new DateOnly( 2026, 1, 1 ),
            Name = "Manual New Year",
            Year = 2026,
            HolidayRuleId = null, // manual
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        await _service.MaterializeDatesAsync( cal.Id, 2026, 2026, ct );

        List<HolidayDate> allDates = await _dbContext.HolidayDates
            .Where( d => d.HolidayCalendarId == cal.Id && d.Year == 2026 )
            .ToListAsync( ct );

        // Should merge: 1 merged (Jan 1) + 2 new (Jul 4, Dec 25) = 3
        Assert.HasCount( 3, allDates );

        // The Jan 1 entry should now have a HolidayRuleId (merged from rule)
        HolidayDate jan1 = allDates.First( d => d.Date == new DateOnly( 2026, 1, 1 ) );
        Assert.IsNotNull( jan1.HolidayRuleId );
    }

    // ── EnsureMaterialized ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task EnsureMaterialized_OnlyRunsOnce( ) {
        CancellationToken ct = TestContext.CancellationTokenSource.Token;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct );

        await _service.EnsureMaterializedAsync( cal.Id, 2026, ct );
        int countAfterFirst = await _dbContext.HolidayDates.CountAsync( d => d.HolidayCalendarId == cal.Id, ct );

        await _service.EnsureMaterializedAsync( cal.Id, 2026, ct );
        int countAfterSecond = await _dbContext.HolidayDates.CountAsync( d => d.HolidayCalendarId == cal.Id, ct );

        Assert.AreEqual( countAfterFirst, countAfterSecond );
    }

    // ── Empty Range ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetDatesForRange_EmptyDateRange_ReturnsEmpty( ) {
        CancellationToken ct = TestContext.CancellationTokenSource.Token;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct );
        await _service.MaterializeDatesAsync( cal.Id, 2026, 2026, ct );

        // Query a range that contains no holidays (March–April has none of our 3 holidays)
        IReadOnlyList<HolidayDate> dates = await _service.GetDatesForRangeAsync(
            cal.Id, new DateOnly( 2026, 3, 1 ), new DateOnly( 2026, 4, 30 ), ct );

        Assert.HasCount( 0, dates );
    }
}
