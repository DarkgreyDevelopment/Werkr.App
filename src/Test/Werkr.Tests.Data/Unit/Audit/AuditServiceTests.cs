using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Data;
using Werkr.Data.Entities.Audit;

namespace Werkr.Tests.Data.Unit.Audit;

[TestClass]
public class AuditServiceTests {

    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private AuditEventTypeRegistry _registry = null!;
    private AuditService _service = null!;

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

        _registry = new AuditEventTypeRegistry( );
        _ = _registry.RegisterCoreAuditEvents( );

        _service = new AuditService(
            _dbContext,
            _registry,
            NullLogger<AuditService>.Instance
        );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    private static AuditEntry MakeEntry(
        string eventTypeId = "auth.login.success",
        string? actorId = "user-123",
        string actorType = "User",
        string? entityType = "User",
        string? entityId = "user-123",
        string action = "Login",
        object? details = null
    ) => new( eventTypeId, actorId, actorType, entityType, entityId, action, details );

    [TestMethod]
    public async Task LogAsync_ValidEntry_PersistsToDatabase( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( ), ct );

        AuditEvent? stored = await _dbContext.AuditEvents.FirstOrDefaultAsync( ct );
        Assert.IsNotNull( stored );
        Assert.AreEqual( "auth.login.success", stored.EventTypeId );
        Assert.AreEqual( "user-123", stored.ActorId );
        Assert.AreEqual( ActorType.User, stored.ActorType );
        Assert.AreEqual( "User", stored.EntityType );
        Assert.AreEqual( "user-123", stored.EntityId );
        Assert.AreEqual( "Login", stored.ActionPerformed );
    }

    [TestMethod]
    public async Task LogAsync_UnregisteredEventType_ThrowsArgumentException( ) {
        CancellationToken ct = TestContext.CancellationToken;

        AuditEntry entry = MakeEntry( eventTypeId: "totally.fake.event" );
        _ = await Assert.ThrowsExactlyAsync<ArgumentException>(
            ( ) => _service.LogAsync( entry, ct ) );
    }

    [TestMethod]
    public async Task LogAsync_NullDetails_StoresEmptyJsonObject( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( details: null ), ct );

        AuditEvent? stored = await _dbContext.AuditEvents.FirstOrDefaultAsync( ct );
        Assert.IsNotNull( stored );
        Assert.AreEqual( "{}", stored.Details );
    }

    [TestMethod]
    public async Task LogAsync_LargeDetails_TruncatedAt8KB( ) {
        CancellationToken ct = TestContext.CancellationToken;

        string largePayload = new( 'x', 10_000 );
        await _service.LogAsync( MakeEntry( details: largePayload ), ct );

        AuditEvent? stored = await _dbContext.AuditEvents.FirstOrDefaultAsync( ct );
        Assert.IsNotNull( stored );
        Assert.IsLessThanOrEqualTo( 8192, stored.Details.Length,
            $"Details should be truncated to 8192 chars, was {stored.Details.Length}." );
    }

    [TestMethod]
    public async Task LogAsync_DetailsObject_SerializedAsJson( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( details: new { Foo = "bar", Count = 42 } ), ct );

        AuditEvent? stored = await _dbContext.AuditEvents.FirstOrDefaultAsync( ct );
        Assert.IsNotNull( stored );
        Assert.Contains( "\"foo\"", stored.Details );
        Assert.Contains( "42", stored.Details );
    }

    [TestMethod]
    public async Task LogAsync_SetsTimestampUtc( ) {
        CancellationToken ct = TestContext.CancellationToken;
        DateTime before = DateTime.UtcNow;

        await _service.LogAsync( MakeEntry( ), ct );

        AuditEvent? stored = await _dbContext.AuditEvents.FirstOrDefaultAsync( ct );
        Assert.IsNotNull( stored );
        Assert.IsGreaterThanOrEqualTo( before.AddSeconds( -1 ), stored.TimestampUtc );
        Assert.IsLessThanOrEqualTo( DateTime.UtcNow.AddSeconds( 1 ), stored.TimestampUtc );
    }

    [TestMethod]
    public async Task LogAsync_PopulatesCategoryAndModuleFromRegistry( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( eventTypeId: "auth.login.success" ), ct );

        AuditEvent? stored = await _dbContext.AuditEvents.FirstOrDefaultAsync( ct );
        Assert.IsNotNull( stored );
        Assert.AreEqual( "Security", stored.EventCategory );
        Assert.AreEqual( "identity", stored.SourceModule );
    }

    [TestMethod]
    public async Task QueryAsync_NoFilters_ReturnsPaginated( ) {
        CancellationToken ct = TestContext.CancellationToken;

        for (int i = 0; i < 5; i++) {
            await _service.LogAsync( MakeEntry( actorId: $"user-{i}" ), ct );
        }

        PagedResult<AuditEventDto> result = await _service.QueryAsync(
            new AuditQuery { Limit = 2, Offset = 0 }, ct );

        Assert.AreEqual( 5, result.TotalCount );
        Assert.HasCount( 2, result.Items );
    }

    [TestMethod]
    public async Task QueryAsync_FilterByEventTypeId_ReturnsOnlyMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( eventTypeId: "auth.login.success" ), ct );
        await _service.LogAsync( MakeEntry( eventTypeId: "auth.login.failure" ), ct );
        await _service.LogAsync( MakeEntry( eventTypeId: "auth.login.success" ), ct );

        PagedResult<AuditEventDto> result = await _service.QueryAsync(
            new AuditQuery { EventTypeId = "auth.login.failure" }, ct );

        Assert.AreEqual( 1, result.TotalCount );
        Assert.AreEqual( "auth.login.failure", result.Items[0].EventTypeId );
    }

    [TestMethod]
    public async Task QueryAsync_FilterByEventCategory_ReturnsOnlyMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( eventTypeId: "auth.login.success" ), ct ); // Security
        await _service.LogAsync( MakeEntry( eventTypeId: "agent.registered" ), ct );   // Agent

        PagedResult<AuditEventDto> result = await _service.QueryAsync(
            new AuditQuery { EventCategory = "Agent" }, ct );

        Assert.AreEqual( 1, result.TotalCount );
    }

    [TestMethod]
    public async Task QueryAsync_FilterByActorId_ReturnsOnlyMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( actorId: "alice" ), ct );
        await _service.LogAsync( MakeEntry( actorId: "bob" ), ct );

        PagedResult<AuditEventDto> result = await _service.QueryAsync(
            new AuditQuery { ActorId = "bob" }, ct );

        Assert.AreEqual( 1, result.TotalCount );
        Assert.AreEqual( "bob", result.Items[0].ActorId );
    }

    [TestMethod]
    public async Task QueryAsync_FilterByEntityTypeAndId_ReturnsOnlyMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( entityType: "Agent", entityId: "a1" ), ct );
        await _service.LogAsync( MakeEntry( entityType: "User", entityId: "u1" ), ct );
        await _service.LogAsync( MakeEntry( entityType: "Agent", entityId: "a2" ), ct );

        PagedResult<AuditEventDto> result = await _service.QueryAsync(
            new AuditQuery { EntityType = "Agent", EntityId = "a1" }, ct );

        Assert.AreEqual( 1, result.TotalCount );
    }

    [TestMethod]
    public async Task QueryAsync_FilterByTimeRange_ReturnsOnlyInRange( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( ), ct );
        DateTime afterFirst = DateTime.UtcNow;

        PagedResult<AuditEventDto> before = await _service.QueryAsync(
            new AuditQuery { ToUtc = afterFirst.AddDays( -1 ) }, ct );
        Assert.AreEqual( 0, before.TotalCount );

        PagedResult<AuditEventDto> after = await _service.QueryAsync(
            new AuditQuery { FromUtc = afterFirst.AddSeconds( -5 ) }, ct );
        Assert.IsGreaterThanOrEqualTo( 1, after.TotalCount );
    }

    [TestMethod]
    public async Task QueryAsync_FilterBySourceModule_ReturnsOnlyMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( eventTypeId: "auth.login.success" ), ct ); // identity
        await _service.LogAsync( MakeEntry( eventTypeId: "agent.registered" ), ct );   // core

        PagedResult<AuditEventDto> result = await _service.QueryAsync(
            new AuditQuery { SourceModule = "core" }, ct );

        Assert.AreEqual( 1, result.TotalCount );
    }

    [TestMethod]
    public async Task QueryAsync_CombinedFilters_IntersectsCorrectly( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( eventTypeId: "auth.login.success", actorId: "alice" ), ct );
        await _service.LogAsync( MakeEntry( eventTypeId: "auth.login.success", actorId: "bob" ), ct );
        await _service.LogAsync( MakeEntry( eventTypeId: "auth.login.failure", actorId: "alice" ), ct );

        PagedResult<AuditEventDto> result = await _service.QueryAsync(
            new AuditQuery { EventTypeId = "auth.login.success", ActorId = "alice" }, ct );

        Assert.AreEqual( 1, result.TotalCount );
    }

    [TestMethod]
    public async Task QueryAsync_Pagination_OffsetSkipsCorrectly( ) {
        CancellationToken ct = TestContext.CancellationToken;

        for (int i = 0; i < 5; i++) {
            await _service.LogAsync( MakeEntry( actorId: $"user-{i}" ), ct );
        }

        PagedResult<AuditEventDto> page1 = await _service.QueryAsync(
            new AuditQuery { Limit = 2, Offset = 0 }, ct );
        PagedResult<AuditEventDto> page2 = await _service.QueryAsync(
            new AuditQuery { Limit = 2, Offset = 2 }, ct );

        Assert.HasCount( 2, page1.Items );
        Assert.HasCount( 2, page2.Items );
        Assert.AreNotEqual( page1.Items[0].Id, page2.Items[0].Id );
    }

    [TestMethod]
    public async Task QueryAsync_OrdersByTimestampDescending( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( actorId: "first" ), ct );
        await _service.LogAsync( MakeEntry( actorId: "second" ), ct );

        PagedResult<AuditEventDto> result = await _service.QueryAsync( new AuditQuery( ), ct );

        Assert.IsGreaterThanOrEqualTo( 2, result.Items.Count );
        Assert.IsGreaterThanOrEqualTo( result.Items[1].TimestampUtc, result.Items[0].TimestampUtc );
    }

    [TestMethod]
    public async Task ExportAsync_JsonFormat_WritesValidJsonArray( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( ), ct );
        await _service.LogAsync( MakeEntry( ), ct );

        using MemoryStream stream = new( );
        await _service.ExportAsync( new AuditQuery( ), ExportFormat.Json, stream, ct );

        stream.Position = 0;
        AuditEventDto[]? items = await System.Text.Json.JsonSerializer
            .DeserializeAsync<AuditEventDto[]>( stream, cancellationToken: ct );

        Assert.IsNotNull( items );
        Assert.HasCount( 2, items );
    }

    [TestMethod]
    public async Task ExportAsync_CsvFormat_WritesHeaderAndRows( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( ), ct );

        using MemoryStream stream = new( );
        await _service.ExportAsync( new AuditQuery( ), ExportFormat.Csv, stream, ct );

        stream.Position = 0;
        using StreamReader reader = new( stream );
        string content = await reader.ReadToEndAsync( ct );
        string[] lines = content.Split( '\n', StringSplitOptions.RemoveEmptyEntries );

        Assert.IsGreaterThanOrEqualTo( 2, lines.Length, "CSV should have header + at least 1 data row." );
        Assert.StartsWith( "Id,", lines[0] );
    }

    [TestMethod]
    public async Task QueryAsync_ExcessiveLimit_ClampedTo200( ) {
        CancellationToken ct = TestContext.CancellationToken;

        for (int i = 0; i < 3; i++) {
            await _service.LogAsync( MakeEntry( actorId: $"user-{i}" ), ct );
        }

        PagedResult<AuditEventDto> result = await _service.QueryAsync(
            new AuditQuery { Limit = 999 }, ct );

        Assert.AreEqual( 200, result.Limit );
    }

    [TestMethod]
    public async Task QueryAsync_ZeroLimit_ClampedTo1( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( ), ct );

        PagedResult<AuditEventDto> result = await _service.QueryAsync(
            new AuditQuery { Limit = 0 }, ct );

        Assert.AreEqual( 1, result.Limit );
    }

    [TestMethod]
    public async Task ExportAsync_CsvFormat_EscapesSpecialCharacters( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( details: "value with, comma and \"quotes\"" ), ct );

        using MemoryStream stream = new( );
        await _service.ExportAsync( new AuditQuery( ), ExportFormat.Csv, stream, ct );

        stream.Position = 0;
        using StreamReader reader = new( stream );
        string content = await reader.ReadToEndAsync( ct );

        Assert.Contains( "\"value with, comma and \"\"quotes\"\"\"", content );
    }

    [TestMethod]
    public async Task QueryAsync_FilterByActorType_ReturnsOnlyMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;

        await _service.LogAsync( MakeEntry( actorType: "User" ), ct );
        await _service.LogAsync( MakeEntry( actorType: "System" ), ct );
        await _service.LogAsync( MakeEntry( actorType: "Agent" ), ct );

        PagedResult<AuditEventDto> result = await _service.QueryAsync(
            new AuditQuery { ActorType = "System" }, ct );

        Assert.AreEqual( 1, result.TotalCount );
        Assert.AreEqual( "System", result.Items[0].ActorType );
    }

    [TestMethod]
    public async Task QueryAsync_FilterByCorrelationId_ReturnsOnlyMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;

        string correlationId = Guid.NewGuid( ).ToString( "N" );
        await _service.LogAsync( MakeEntry( ) with { CorrelationId = correlationId }, ct );
        await _service.LogAsync( MakeEntry( ) with { CorrelationId = "other-id" }, ct );
        await _service.LogAsync( MakeEntry( ), ct );

        PagedResult<AuditEventDto> result = await _service.QueryAsync(
            new AuditQuery { CorrelationId = correlationId }, ct );

        Assert.AreEqual( 1, result.TotalCount );
        Assert.AreEqual( correlationId, result.Items[0].CorrelationId );
    }
}
