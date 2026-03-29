using Werkr.Common.Models;
using Werkr.Common.Models.Audit;

namespace Werkr.Core.Audit;

/// <summary>
/// Service for recording and querying audit events.
/// </summary>
public interface IAuditService {
    /// <summary>
    /// Records an audit event. Does NOT swallow exceptions — the caller must handle failure
    /// to implement the "audit must succeed before operation commits" pattern.
    /// </summary>
    Task LogAsync( AuditEntry entry, CancellationToken ct = default );

    /// <summary>
    /// Queries audit events with filtering and pagination.
    /// </summary>
    Task<PagedResult<AuditEventDto>> QueryAsync( AuditQuery query, CancellationToken ct = default );

    /// <summary>
    /// Exports audit events as a stream (JSON or CSV). Optionally limited to <paramref name="maxRows"/> rows.
    /// </summary>
    Task ExportAsync( AuditQuery query, ExportFormat format, Stream outputStream, CancellationToken ct = default, int? maxRows = null );

    /// <summary>
    /// Returns the distinct entity types that appear in the audit log, sorted alphabetically.
    /// </summary>
    Task<IReadOnlyList<string>> GetEntityTypesAsync( CancellationToken ct = default );
}
