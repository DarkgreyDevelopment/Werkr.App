using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Models;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Core.Tasks;

/// <summary>
/// Computes property-level diffs between two <see cref="TaskVersion"/> snapshots.
/// </summary>
/// <param name="dbContext">Database context.</param>
public sealed class TaskVersionDiffService( WerkrDbContext dbContext ) {

    /// <summary>
    /// Computes the differences between two task versions.
    /// Both versions must belong to the same task.
    /// </summary>
    /// <param name="fromVersionId">The baseline version ID.</param>
    /// <param name="toVersionId">The target version ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of property-level differences, or null if either version is not found.</returns>
    public async Task<IReadOnlyList<TaskVersionDiffEntry>?> ComputeDiffAsync(
        long fromVersionId,
        long toVersionId,
        CancellationToken ct = default
    ) {
        TaskVersion? from = await dbContext.TaskVersions
            .AsNoTracking( )
            .FirstOrDefaultAsync( v => v.Id == fromVersionId, ct );
        TaskVersion? to = await dbContext.TaskVersions
            .AsNoTracking( )
            .FirstOrDefaultAsync( v => v.Id == toVersionId, ct );

        if (from is null || to is null) {
            return null;
        }

        TaskDefinitionSnapshot? fromSnapshot = TaskDefinitionSnapshot.FromJson( from.Definition );
        TaskDefinitionSnapshot? toSnapshot = TaskDefinitionSnapshot.FromJson( to.Definition );

        if (fromSnapshot is null || toSnapshot is null) {
            return null;
        }

        return ComputeDiff( fromSnapshot, toSnapshot );
    }

    /// <summary>
    /// Computes property-level differences between two snapshots.
    /// </summary>
    internal static IReadOnlyList<TaskVersionDiffEntry> ComputeDiff(
        TaskDefinitionSnapshot from,
        TaskDefinitionSnapshot to
    ) {
        List<TaskVersionDiffEntry> entries = [];

        CompareScalar( entries, nameof( TaskDefinitionSnapshot.Name ), from.Name, to.Name );
        CompareScalar( entries, nameof( TaskDefinitionSnapshot.Description ), from.Description, to.Description );
        CompareScalar( entries, nameof( TaskDefinitionSnapshot.ActionType ), from.ActionType, to.ActionType );
        CompareScalar( entries, nameof( TaskDefinitionSnapshot.Content ), from.Content, to.Content );
        CompareScalar( entries, nameof( TaskDefinitionSnapshot.Enabled ), from.Enabled.ToString( ), to.Enabled.ToString( ) );
        CompareScalar( entries, nameof( TaskDefinitionSnapshot.TimeoutMinutes ), from.TimeoutMinutes?.ToString( ), to.TimeoutMinutes?.ToString( ) );
        CompareScalar( entries, nameof( TaskDefinitionSnapshot.SuccessCriteria ), from.SuccessCriteria, to.SuccessCriteria );
        CompareScalar( entries, nameof( TaskDefinitionSnapshot.ActionSubType ), from.ActionSubType, to.ActionSubType );
        CompareScalar( entries, nameof( TaskDefinitionSnapshot.WorkflowId ), from.WorkflowId?.ToString( ), to.WorkflowId?.ToString( ) );
        CompareArray( entries, nameof( TaskDefinitionSnapshot.Arguments ), from.Arguments, to.Arguments, sorted: false );
        CompareArray( entries, nameof( TaskDefinitionSnapshot.TargetTags ), from.TargetTags, to.TargetTags, sorted: true );
        CompareJson( entries, nameof( TaskDefinitionSnapshot.ActionParameters ), from.ActionParameters, to.ActionParameters );

        return entries;
    }

    private static void CompareScalar( List<TaskVersionDiffEntry> entries, string path, string? oldVal, string? newVal ) {
        if (string.Equals( oldVal, newVal, StringComparison.Ordinal )) {
            return;
        }

        DiffChangeType changeType = (oldVal, newVal) switch {
            (null or "", not null and not "") => DiffChangeType.Added,
            (not null and not "", null or "") => DiffChangeType.Removed,
            _ => DiffChangeType.Modified,
        };

        entries.Add( new TaskVersionDiffEntry( path, oldVal, newVal, changeType ) );
    }

    private static void CompareArray( List<TaskVersionDiffEntry> entries, string path, string[]? oldArr, string[]? newArr, bool sorted = true ) {
        string oldJson = NormalizeArray( oldArr, sorted );
        string newJson = NormalizeArray( newArr, sorted );

        if (string.Equals( oldJson, newJson, StringComparison.Ordinal )) {
            return;
        }

        DiffChangeType changeType = (oldArr, newArr) switch {
            (null, not null) => DiffChangeType.Added,
            (not null, null) => DiffChangeType.Removed,
            _ => DiffChangeType.Modified,
        };

        entries.Add( new TaskVersionDiffEntry( path, oldJson, newJson, changeType ) );
    }

    private static void CompareJson( List<TaskVersionDiffEntry> entries, string path, string? oldJson, string? newJson ) {
        string? normalizedOld = NormalizeJson( oldJson );
        string? normalizedNew = NormalizeJson( newJson );

        if (string.Equals( normalizedOld, normalizedNew, StringComparison.Ordinal )) {
            return;
        }

        DiffChangeType changeType = (normalizedOld, normalizedNew) switch {
            (null, not null) => DiffChangeType.Added,
            (not null, null) => DiffChangeType.Removed,
            _ => DiffChangeType.Modified,
        };

        entries.Add( new TaskVersionDiffEntry( path, normalizedOld, normalizedNew, changeType ) );
    }

    private static string NormalizeArray( string[]? arr, bool sorted = true ) {
        if (arr is null || arr.Length == 0) {
            return "[]";
        }
        if (sorted) {
            string[] ordered = [.. arr.OrderBy( s => s, StringComparer.Ordinal )];
            return JsonSerializer.Serialize( ordered );
        }
        return JsonSerializer.Serialize( arr );
    }

    private static string? NormalizeJson( string? json ) {
        if (string.IsNullOrWhiteSpace( json )) {
            return null;
        }

        try {
            using JsonDocument doc = JsonDocument.Parse( json );
            return JsonSerializer.Serialize( doc.RootElement );
        } catch (JsonException) {
            return json;
        }
    }
}
