using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Data;
using Werkr.Data.Entities.Audit;

namespace Werkr.Core.Audit;

/// <summary>
/// Scoped service for recording and querying audit events.
/// </summary>
public sealed partial class AuditService(
    WerkrDbContext dbContext,
    IAuditEventTypeRegistry registry,
    ILogger<AuditService> logger
) : IAuditService {

    /// <summary>Maximum allowed size for serialized Details JSON payload.</summary>
    private const int MaxDetailsBytes = 8192;

    private static readonly JsonSerializerOptions s_detailsOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 8,
        WriteIndented = false
    };

    /// <inheritdoc/>
    public async Task LogAsync( AuditEntry entry, CancellationToken ct = default ) {
        AuditEventTypeDto? typeDef = registry.GetByTypeId( entry.EventTypeId ) ?? throw new ArgumentException( $"Audit event type '{entry.EventTypeId}' is not registered.", nameof( entry ) );
        string? detailsJson = null;
        if (entry.Details is not null) {
            detailsJson = entry.Details is string s
                ? s
                : JsonSerializer.Serialize( entry.Details, s_detailsOptions );

            if (detailsJson.Length > MaxDetailsBytes) {
                LogDetailsTruncated( logger, entry.EventTypeId, detailsJson.Length, MaxDetailsBytes );
                detailsJson = detailsJson[..MaxDetailsBytes];
            }
        }

        AuditEvent entity = new( ) {
            EventTypeId = Sanitize( entry.EventTypeId, 128 ),
            EventCategory = Sanitize( typeDef.Category, 64 ),
            SourceModule = Sanitize( typeDef.SourceModule, 64 ),
            ActorId = entry.ActorId is not null ? Sanitize( entry.ActorId, 128 ) : null,
            ActorType = Enum.TryParse<ActorType>( entry.ActorType, ignoreCase: true, out ActorType parsed )
                ? parsed : ActorType.System,
            EntityType = entry.EntityType is not null ? Sanitize( entry.EntityType, 64 ) : null,
            EntityId = entry.EntityId is not null ? Sanitize( entry.EntityId, 128 ) : null,
            ActionPerformed = Sanitize( entry.ActionPerformed, 64 ),
            Details = detailsJson ?? "{}",
            TimestampUtc = DateTime.UtcNow,
            CorrelationId = entry.CorrelationId is not null ? Sanitize( entry.CorrelationId, 128 ) : null
        };

        _ = dbContext.AuditEvents.Add( entity );
        _ = await dbContext.SaveChangesAsync( ct );
    }

    /// <inheritdoc/>
    public async Task<PagedResult<AuditEventDto>> QueryAsync( AuditQuery query, CancellationToken ct = default ) {
        int limit = Math.Clamp( query.Limit, 1, 200 );
        int offset = query.Offset;

        IQueryable<AuditEvent> q = BuildQuery( query );

        int totalCount = await q.CountAsync( ct );
        List<AuditEvent> items = await q
            .OrderByDescending( e => e.TimestampUtc )
            .Skip( offset )
            .Take( limit )
            .AsNoTracking( )
            .ToListAsync( ct );

        IReadOnlyList<AuditEventDto> dtos = [.. items.Select( ToDto )];
        return new PagedResult<AuditEventDto>( dtos, totalCount, limit, offset );
    }

    /// <inheritdoc/>
    public async Task ExportAsync( AuditQuery query, ExportFormat format, Stream outputStream, CancellationToken ct = default, int? maxRows = null ) {
        IQueryable<AuditEvent> q = BuildQuery( query ).OrderByDescending( e => e.TimestampUtc ).AsNoTracking( );

        if (maxRows.HasValue) {
            q = q.Take( maxRows.Value );
        }

        if (format == ExportFormat.Csv) {
            await WriteCsvAsync( q, outputStream, ct );
        } else {
            await WriteJsonAsync( q, outputStream, ct );
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetEntityTypesAsync( CancellationToken ct = default ) {
        return await dbContext.AuditEvents
            .Where( e => e.EntityType != null )
            .Select( e => e.EntityType! )
            .Distinct( )
            .OrderBy( t => t )
            .ToListAsync( ct );
    }

    private IQueryable<AuditEvent> BuildQuery( AuditQuery query ) {
        IQueryable<AuditEvent> q = dbContext.AuditEvents;

        if (!string.IsNullOrWhiteSpace( query.EventTypeId )) {
            q = q.Where( e => e.EventTypeId == query.EventTypeId );
        }
        if (!string.IsNullOrWhiteSpace( query.EventCategory )) {
            q = q.Where( e => e.EventCategory == query.EventCategory );
        }
        if (!string.IsNullOrWhiteSpace( query.SourceModule )) {
            q = q.Where( e => e.SourceModule == query.SourceModule );
        }
        if (!string.IsNullOrWhiteSpace( query.ActorId )) {
            q = q.Where( e => e.ActorId == query.ActorId );
        }
        if (!string.IsNullOrWhiteSpace( query.ActorType )) {
            if (Enum.TryParse<ActorType>( query.ActorType, ignoreCase: true, out ActorType actorType )) {
                q = q.Where( e => e.ActorType == actorType );
            }
        }
        if (!string.IsNullOrWhiteSpace( query.EntityType )) {
            q = q.Where( e => e.EntityType == query.EntityType );
        }
        if (!string.IsNullOrWhiteSpace( query.EntityId )) {
            q = q.Where( e => e.EntityId == query.EntityId );
        }
        if (!string.IsNullOrWhiteSpace( query.CorrelationId )) {
            q = q.Where( e => e.CorrelationId == query.CorrelationId );
        }
        if (query.FromUtc.HasValue) {
            q = q.Where( e => e.TimestampUtc >= query.FromUtc.Value );
        }
        if (query.ToUtc.HasValue) {
            q = q.Where( e => e.TimestampUtc <= query.ToUtc.Value );
        }

        return q;
    }

    private static async Task WriteJsonAsync( IQueryable<AuditEvent> query, Stream stream, CancellationToken ct ) {
        await using Utf8JsonWriter writer = new( stream );
        writer.WriteStartArray( );

        await foreach (AuditEvent e in query.AsAsyncEnumerable( ).WithCancellation( ct )) {
            JsonSerializer.Serialize( writer, ToDto( e ), s_detailsOptions );
        }

        writer.WriteEndArray( );
        await writer.FlushAsync( ct );
    }

    private static async Task WriteCsvAsync( IQueryable<AuditEvent> query, Stream stream, CancellationToken ct ) {
        await using StreamWriter writer = new( stream, Encoding.UTF8, leaveOpen: true );
        await writer.WriteLineAsync( "Id,TimestampUtc,EventTypeId,EventCategory,SourceModule,ActorId,ActorType,EntityType,EntityId,ActionPerformed,Details,CorrelationId" );

        await foreach (AuditEvent e in query.AsAsyncEnumerable( ).WithCancellation( ct )) {
            await writer.WriteLineAsync( string.Create( CultureInfo.InvariantCulture,
                $"{e.Id},{e.TimestampUtc:O},{CsvEscape( e.EventTypeId )},{CsvEscape( e.EventCategory )},{CsvEscape( e.SourceModule )},{CsvEscape( e.ActorId )},{e.ActorType},{CsvEscape( e.EntityType )},{CsvEscape( e.EntityId )},{CsvEscape( e.ActionPerformed )},{CsvEscape( e.Details )},{CsvEscape( e.CorrelationId )}" ) );
        }

        await writer.FlushAsync( ct );
    }

    private static AuditEventDto ToDto( AuditEvent e ) => new(
        e.Id,
        e.EventTypeId,
        e.EventCategory,
        e.SourceModule,
        e.ActorId,
        e.ActorType.ToString( ),
        e.EntityType,
        e.EntityId,
        e.ActionPerformed,
        e.Details,
        e.TimestampUtc,
        e.CorrelationId
    );

    /// <summary>Trims, enforces max length, and strips control characters.</summary>
    private static string Sanitize( string input, int maxLength ) {
        string trimmed = input.Trim( );
        if (trimmed.Length > maxLength) {
            trimmed = trimmed[..maxLength];
        }
        return StripControlChars( trimmed );
    }

    private static string StripControlChars( string input ) {
        StringBuilder? sb = null;
        for (int i = 0; i < input.Length; i++) {
            char c = input[i];
            if (char.IsControl( c ) && c != '\n' && c != '\r' && c != '\t') {
                sb ??= new StringBuilder( input[..i] );
            } else {
                _ = (sb?.Append( c ));
            }
        }
        return sb?.ToString( ) ?? input;
    }

    private static string CsvEscape( string? value ) {
        return string.IsNullOrEmpty( value )
            ? string.Empty
            : value.Contains( '"' ) || value.Contains( ',' ) || value.Contains( '\n' ) ? $"\"{value.Replace( "\"", "\"\"" )}\"" : value;
    }

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Audit event '{EventTypeId}' details ({ActualSize} bytes) exceeded max ({MaxSize} bytes) and were truncated." )]
    private static partial void LogDetailsTruncated( ILogger logger, string eventTypeId, int actualSize, int maxSize );
}
