using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Api.Services;
using Werkr.Core.Communication;
using Werkr.Core.Scheduling;
using Werkr.Data;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Tests.Data.Unit.Scheduling;

[TestClass]
public class HolidayCalendarServiceTests {
    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private HolidayCalendarService _service = null!;

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

        HolidayDateService dateService = new(
            _dbContext,
            NullLogger<HolidayDateService>.Instance );

        // Build a minimal service provider for ScheduleInvalidationDispatcher (it needs IServiceScopeFactory).
        // Register WerkrDbContext as *scoped* so each scope gets its own instance, avoiding
        // concurrent-access errors when Task.Run inside DeleteAsync resolves a second DbContext.
        // All instances share the same SQLite connection, so data is visible across contexts.
        ServiceCollection services = new( );
        _ = services.AddScoped<WerkrDbContext>( _ => {
            DbContextOptions<SqliteWerkrDbContext> scopeOpts =
                new DbContextOptionsBuilder<SqliteWerkrDbContext>( )
                    .UseSqlite( _connection )
                    .Options;
            return new SqliteWerkrDbContext( scopeOpts );
        } );
        ServiceProvider sp = services.BuildServiceProvider( );

        AgentConnectionManager connMgr = new(
            sp.GetRequiredService<IServiceScopeFactory>( ),
            NullLogger<AgentConnectionManager>.Instance );

        ScheduleInvalidationDispatcher dispatcher = new(
            connMgr,
            sp.GetRequiredService<IServiceScopeFactory>( ),
            NullLogger<ScheduleInvalidationDispatcher>.Instance );

        _service = new HolidayCalendarService(
            _dbContext,
            dateService,
            dispatcher,
            NullLogger<HolidayCalendarService>.Instance );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    #region Helpers

    private async Task<HolidayCalendar> SeedCalendarAsync( string name = "Test Calendar", bool isSystem = false, CancellationToken ct = default ) {
        HolidayCalendar cal = new( ) {
            Id = Guid.NewGuid( ),
            Name = name,
            Description = "Test description",
            IsSystemCalendar = isSystem,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };
        _ = _dbContext.HolidayCalendars.Add( cal );
        _ = await _dbContext.SaveChangesAsync( ct );
        return cal;
    }

    private async Task<HolidayCalendar> SeedCalendarWithRulesAsync( string name = "Ruled Calendar", CancellationToken ct = default ) {
        HolidayCalendar cal = await SeedCalendarAsync( name, ct: ct );
        _ = _dbContext.HolidayRules.Add( new HolidayRule {
            HolidayCalendarId = cal.Id,
            Name = "New Year",
            RuleType = HolidayRuleType.FixedDate,
            Month = 1,
            Day = 1,
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

    private async Task<DbSchedule> SeedScheduleAsync( CancellationToken ct = default ) {
        DbSchedule sched = new( ) {
            Id = Guid.NewGuid( ),
            Name = "Test Schedule",
            StopTaskAfterMinutes = 60,
            Created = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
        };
        _ = _dbContext.Schedules.Add( sched );
        _ = await _dbContext.SaveChangesAsync( ct );
        return sched;
    }

    #endregion

    // ── Calendar CRUD ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateAsync_SetsIsSystemFalse( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = new( ) {
            Name = "Custom",
            Description = "Custom calendar",
            IsSystemCalendar = true, // attempting to set true
        };

        HolidayCalendar result = await _service.CreateAsync( cal, ct );

        Assert.IsFalse( result.IsSystemCalendar );
        Assert.AreNotEqual( default, result.CreatedUtc );
    }

    [TestMethod]
    public async Task UpdateAsync_UpdatesNameAndDescription( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( ct: ct );
        HolidayCalendar updated = new( ) { Name = "Updated Name", Description = "Updated Desc" };

        HolidayCalendar result = await _service.UpdateAsync( cal.Id, updated, ct );

        Assert.AreEqual( "Updated Name", result.Name );
        Assert.AreEqual( "Updated Desc", result.Description );
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesCalendar( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( ct: ct );

        await _service.DeleteAsync( cal.Id, ct );

        HolidayCalendar? found = await _dbContext.HolidayCalendars.FindAsync( [cal.Id], ct );
        Assert.IsNull( found );
    }

    [TestMethod]
    public async Task GetByIdAsync_ReturnsWithRulesAndDates( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct: ct );

        HolidayCalendar? found = await _service.GetByIdAsync( cal.Id, ct );

        Assert.IsNotNull( found );
        Assert.HasCount( 2, found.Rules );
    }

    [TestMethod]
    public async Task GetAllAsync_ReturnsList( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await SeedCalendarAsync( "Cal A", ct: ct );
        _ = await SeedCalendarAsync( "Cal B", ct: ct );

        IReadOnlyList<HolidayCalendar> all = await _service.GetAllAsync( ct );

        Assert.HasCount( 2, all );
    }

    // ── System Calendar Protection ─────────────────────────────────────────────

    [TestMethod]
    public async Task UpdateAsync_SystemCalendar_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( "System", true, ct );
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            ( ) => _service.UpdateAsync( cal.Id, new HolidayCalendar { Name = "X" }, ct ) );
    }

    [TestMethod]
    public async Task DeleteAsync_SystemCalendar_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( "System", true, ct );
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            ( ) => _service.DeleteAsync( cal.Id, ct ) );
    }

    // ── Clone ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CloneAsync_CopiesRulesAsNonSystem( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar source = await SeedCalendarWithRulesAsync( "Source", ct: ct );

        HolidayCalendar clone = await _service.CloneAsync( source.Id, "Clone of Source", ct );

        Assert.AreNotEqual( source.Id, clone.Id );
        Assert.AreEqual( "Clone of Source", clone.Name );
        Assert.IsFalse( clone.IsSystemCalendar );

        // Verify rules were cloned
        HolidayCalendar? loaded = await _service.GetByIdAsync( clone.Id, ct );
        Assert.IsNotNull( loaded );
        Assert.HasCount( 2, loaded.Rules );
    }

    [TestMethod]
    public async Task CloneAsync_SystemCalendar_CanBeCloned( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar system = await SeedCalendarAsync( "US Federal", true, ct );
        _ = _dbContext.HolidayRules.Add( new HolidayRule {
            HolidayCalendarId = system.Id,
            Name = "July 4",
            RuleType = HolidayRuleType.FixedDate,
            Month = 7,
            Day = 4,
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        HolidayCalendar clone = await _service.CloneAsync( system.Id, "My Federal Holidays", ct );

        Assert.IsFalse( clone.IsSystemCalendar );
        HolidayCalendar? loaded = await _service.GetByIdAsync( clone.Id, ct );
        Assert.IsNotNull( loaded );
        Assert.HasCount( 1, loaded.Rules );
    }

    // ── Rule Operations ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AddRuleAsync_AddsAndInvalidatesCache( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( ct: ct );

        HolidayRule rule = new( ) {
            Name = "July 4",
            RuleType = HolidayRuleType.FixedDate,
            Month = 7,
            Day = 4,
        };

        HolidayRule added = await _service.AddRuleAsync( cal.Id, rule, ct );

        Assert.AreNotEqual( 0, added.Id );
        Assert.AreEqual( cal.Id, added.HolidayCalendarId );
    }

    [TestMethod]
    public async Task AddRuleAsync_SystemCalendar_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( "System", true, ct );
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            ( ) => _service.AddRuleAsync( cal.Id, new HolidayRule {
                Name = "Test",
                RuleType = HolidayRuleType.FixedDate,
                Month = 1,
                Day = 1,
            }, ct ) );
    }

    [TestMethod]
    public async Task AddRuleAsync_InvalidRule_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( ct: ct );
        _ = await Assert.ThrowsExactlyAsync<System.ComponentModel.DataAnnotations.ValidationException>(
            ( ) => _service.AddRuleAsync( cal.Id, new HolidayRule {
                Name = "", // empty name fails validation
                RuleType = HolidayRuleType.FixedDate,
                Month = 1,
                Day = 1,
            }, ct ) );
    }

    [TestMethod]
    public async Task UpdateRuleAsync_UpdatesFields( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct: ct );
        List<HolidayRule> rules = await _dbContext.HolidayRules
            .Where( r => r.HolidayCalendarId == cal.Id )
            .ToListAsync( ct );

        HolidayRule ruleToUpdate = rules[0];
        HolidayRule updated = new( ) {
            Name = "Updated Name",
            RuleType = ruleToUpdate.RuleType,
            Month = ruleToUpdate.Month,
            Day = ruleToUpdate.Day,
        };

        HolidayRule result = await _service.UpdateRuleAsync( cal.Id, ruleToUpdate.Id, updated, ct );
        Assert.AreEqual( "Updated Name", result.Name );
    }

    [TestMethod]
    public async Task RemoveRuleAsync_DeletesRule( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct: ct );
        List<HolidayRule> rules = await _dbContext.HolidayRules
            .Where( r => r.HolidayCalendarId == cal.Id )
            .ToListAsync( ct );

        await _service.RemoveRuleAsync( cal.Id, rules[0].Id, ct );

        int remaining = await _dbContext.HolidayRules
            .CountAsync( r => r.HolidayCalendarId == cal.Id, ct );
        Assert.AreEqual( 1, remaining );
    }

    // ── Manual Date Operations ─────────────────────────────────────────────────

    [TestMethod]
    public async Task AddManualDateAsync_AddsDate( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( ct: ct );

        HolidayDate date = new( ) {
            Date = new DateOnly( 2026, 3, 15 ),
            Name = "Company Holiday",
            Year = 2026,
        };

        HolidayDate added = await _service.AddManualDateAsync( cal.Id, date, ct );

        Assert.AreNotEqual( 0, added.Id );
        Assert.IsNull( added.HolidayRuleId );
        Assert.AreEqual( cal.Id, added.HolidayCalendarId );
    }

    [TestMethod]
    public async Task RemoveManualDateAsync_RemovesDate( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( ct: ct );
        HolidayDate date = new( ) {
            HolidayCalendarId = cal.Id,
            Date = new DateOnly( 2026, 3, 15 ),
            Name = "Company Holiday",
            Year = 2026,
        };
        _ = _dbContext.HolidayDates.Add( date );
        _ = await _dbContext.SaveChangesAsync( ct );

        await _service.RemoveManualDateAsync( cal.Id, date.Id, ct );

        int count = await _dbContext.HolidayDates.CountAsync( d => d.HolidayCalendarId == cal.Id, ct );
        Assert.AreEqual( 0, count );
    }

    // ── Attach / Detach ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AttachToScheduleAsync_CreatesLink( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( ct: ct );
        DbSchedule sched = await SeedScheduleAsync( ct );

        ScheduleHolidayCalendar link = await _service.AttachToScheduleAsync(
            sched.Id, cal.Id, HolidayCalendarMode.Blocklist, ct );

        Assert.AreEqual( sched.Id, link.ScheduleId );
        Assert.AreEqual( cal.Id, link.HolidayCalendarId );
        Assert.AreEqual( HolidayCalendarMode.Blocklist, link.Mode );
    }

    [TestMethod]
    public async Task DetachFromScheduleAsync_RemovesLink( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( ct: ct );
        DbSchedule sched = await SeedScheduleAsync( ct );

        _ = await _service.AttachToScheduleAsync(
            sched.Id, cal.Id, HolidayCalendarMode.Blocklist, ct );

        await _service.DetachFromScheduleAsync( sched.Id, ct );

        ScheduleHolidayCalendar? link = await _dbContext.ScheduleHolidayCalendars
            .FirstOrDefaultAsync( l => l.ScheduleId == sched.Id, ct );
        Assert.IsNull( link );
    }

    [TestMethod]
    public async Task DetachFromScheduleAsync_NoLink_NoException( ) {
        CancellationToken ct = TestContext.CancellationToken;
        DbSchedule sched = await SeedScheduleAsync( ct );

        // Should not throw
        await _service.DetachFromScheduleAsync( sched.Id, ct );
    }

    [TestMethod]
    public async Task GetScheduleCalendarAsync_ReturnsAttachedCalendar( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( ct: ct );
        DbSchedule sched = await SeedScheduleAsync( ct );

        _ = await _service.AttachToScheduleAsync(
            sched.Id, cal.Id, HolidayCalendarMode.Allowlist, ct );

        ScheduleHolidayCalendar? result = await _service.GetScheduleCalendarAsync( sched.Id, ct );

        Assert.IsNotNull( result );
        Assert.AreEqual( HolidayCalendarMode.Allowlist, result.Mode );
        Assert.IsNotNull( result.Calendar );
        Assert.AreEqual( cal.Name, result.Calendar.Name );
    }

    [TestMethod]
    public async Task GetScheduleCalendarAsync_NoAttachment_ReturnsNull( ) {
        CancellationToken ct = TestContext.CancellationToken;
        DbSchedule sched = await SeedScheduleAsync( ct );
        ScheduleHolidayCalendar? result = await _service.GetScheduleCalendarAsync( sched.Id, ct );
        Assert.IsNull( result );
    }

    // ── Cascade Delete ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task DeleteAsync_DetachesFromSchedules( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarAsync( ct: ct );
        DbSchedule sched = await SeedScheduleAsync( ct );

        _ = await _service.AttachToScheduleAsync(
            sched.Id, cal.Id, HolidayCalendarMode.Blocklist, ct );

        await _service.DeleteAsync( cal.Id, ct );

        ScheduleHolidayCalendar? link = await _dbContext.ScheduleHolidayCalendars
            .FirstOrDefaultAsync( l => l.ScheduleId == sched.Id, ct );
        Assert.IsNull( link );
    }

    // ── Preview ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task PreviewDatesAsync_IncludesRulesAndManual( ) {
        CancellationToken ct = TestContext.CancellationToken;
        HolidayCalendar cal = await SeedCalendarWithRulesAsync( ct: ct );
        _ = _dbContext.HolidayDates.Add( new HolidayDate {
            HolidayCalendarId = cal.Id,
            Date = new DateOnly( 2026, 6, 19 ),
            Name = "Juneteenth",
            Year = 2026,
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        IReadOnlyList<HolidayDate> preview = await _service.PreviewDatesAsync( cal.Id, 2026, 2026, ct );

        // 2 rules (Jan 1, Dec 25) + 1 manual = 3
        Assert.HasCount( 3, preview );
    }

    [TestMethod]
    public void PreviewRuleAsync_ReturnsComputedDates( ) {
        HolidayRule rule = new( ) {
            Name = "July 4",
            RuleType = HolidayRuleType.FixedDate,
            Month = 7,
            Day = 4,
        };

        IReadOnlyList<HolidayDate> preview = HolidayCalendarService.PreviewRuleAsync( rule, 2025, 2027 );

        Assert.HasCount( 3, preview );
    }
}
