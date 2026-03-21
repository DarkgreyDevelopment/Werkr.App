using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Models;
using Werkr.Data;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Workflows;

/// <summary>
/// Computes property-level diffs between two <see cref="WorkflowVersion"/> snapshots.
/// </summary>
/// <param name="dbContext">Database context.</param>
public sealed class WorkflowVersionDiffService( WerkrDbContext dbContext ) {

    /// <summary>
    /// Computes the differences between two workflow versions.
    /// Both versions must belong to the same workflow.
    /// </summary>
    public async Task<IReadOnlyList<WorkflowVersionDiffEntry>?> ComputeDiffAsync(
        long fromVersionId,
        long toVersionId,
        CancellationToken ct = default
    ) {
        WorkflowVersion? from = await dbContext.Set<WorkflowVersion>( )
            .AsNoTracking( )
            .FirstOrDefaultAsync( v => v.Id == fromVersionId, ct );
        WorkflowVersion? to = await dbContext.Set<WorkflowVersion>( )
            .AsNoTracking( )
            .FirstOrDefaultAsync( v => v.Id == toVersionId, ct );

        if (from is null || to is null) {
            return null;
        }

        WorkflowDefinitionSnapshot? fromSnapshot = WorkflowDefinitionSnapshot.FromJson( from.Definition );
        WorkflowDefinitionSnapshot? toSnapshot = WorkflowDefinitionSnapshot.FromJson( to.Definition );

        if (fromSnapshot is null || toSnapshot is null) {
            return null;
        }

        return ComputeDiff( fromSnapshot, toSnapshot );
    }

    /// <summary>
    /// Computes property-level differences between two workflow snapshots.
    /// </summary>
    internal static IReadOnlyList<WorkflowVersionDiffEntry> ComputeDiff(
        WorkflowDefinitionSnapshot from,
        WorkflowDefinitionSnapshot to
    ) {
        List<WorkflowVersionDiffEntry> entries = [];

        CompareScalar( entries, nameof( WorkflowDefinitionSnapshot.Name ), from.Name, to.Name );
        CompareScalar( entries, nameof( WorkflowDefinitionSnapshot.Description ), from.Description, to.Description );
        CompareScalar( entries, nameof( WorkflowDefinitionSnapshot.Enabled ), from.Enabled.ToString( ), to.Enabled.ToString( ) );
        CompareArray( entries, nameof( WorkflowDefinitionSnapshot.TargetTags ), from.TargetTags, to.TargetTags );
        CompareJson( entries, nameof( WorkflowDefinitionSnapshot.Annotations ), from.Annotations, to.Annotations );

        // Compare steps by StepId
        CompareSteps( entries, from.Steps, to.Steps );

        // Compare edges
        CompareEdges( entries, from.Edges, to.Edges );

        // Compare variables by Name
        CompareVariables( entries, from.Variables, to.Variables );

        return entries;
    }

    private static void CompareSteps(
        List<WorkflowVersionDiffEntry> entries,
        WorkflowStepSnapshot[]? fromSteps,
        WorkflowStepSnapshot[]? toSteps
    ) {
        Dictionary<long, WorkflowStepSnapshot> fromMap = (fromSteps ?? []).ToDictionary( s => s.StepId );
        Dictionary<long, WorkflowStepSnapshot> toMap = (toSteps ?? []).ToDictionary( s => s.StepId );

        foreach (long stepId in fromMap.Keys.Except( toMap.Keys )) {
            entries.Add( new WorkflowVersionDiffEntry(
                $"Steps[{stepId}]", JsonSerializer.Serialize( fromMap[stepId] ), null, DiffChangeType.Removed ) );
        }

        foreach (long stepId in toMap.Keys.Except( fromMap.Keys )) {
            entries.Add( new WorkflowVersionDiffEntry(
                $"Steps[{stepId}]", null, JsonSerializer.Serialize( toMap[stepId] ), DiffChangeType.Added ) );
        }

        foreach (long stepId in fromMap.Keys.Intersect( toMap.Keys )) {
            string oldJson = JsonSerializer.Serialize( fromMap[stepId] );
            string newJson = JsonSerializer.Serialize( toMap[stepId] );
            if (!string.Equals( oldJson, newJson, StringComparison.Ordinal )) {
                entries.Add( new WorkflowVersionDiffEntry(
                    $"Steps[{stepId}]", oldJson, newJson, DiffChangeType.Modified ) );
            }
        }
    }

    private static void CompareEdges(
        List<WorkflowVersionDiffEntry> entries,
        WorkflowEdgeSnapshot[]? fromEdges,
        WorkflowEdgeSnapshot[]? toEdges
    ) {
        HashSet<string> fromSet = [.. (fromEdges ?? []).Select( e => $"{e.StepId}->{e.DependsOnStepId}" )];
        HashSet<string> toSet = [.. (toEdges ?? []).Select( e => $"{e.StepId}->{e.DependsOnStepId}" )];

        foreach (string edge in fromSet.Except( toSet )) {
            entries.Add( new WorkflowVersionDiffEntry( $"Edges[{edge}]", edge, null, DiffChangeType.Removed ) );
        }

        foreach (string edge in toSet.Except( fromSet )) {
            entries.Add( new WorkflowVersionDiffEntry( $"Edges[{edge}]", null, edge, DiffChangeType.Added ) );
        }
    }

    private static void CompareVariables(
        List<WorkflowVersionDiffEntry> entries,
        WorkflowVariableSnapshot[]? fromVars,
        WorkflowVariableSnapshot[]? toVars
    ) {
        Dictionary<string, WorkflowVariableSnapshot> fromMap = (fromVars ?? [])
            .ToDictionary( v => v.Name, StringComparer.OrdinalIgnoreCase );
        Dictionary<string, WorkflowVariableSnapshot> toMap = (toVars ?? [])
            .ToDictionary( v => v.Name, StringComparer.OrdinalIgnoreCase );

        foreach (string name in fromMap.Keys.Except( toMap.Keys, StringComparer.OrdinalIgnoreCase )) {
            entries.Add( new WorkflowVersionDiffEntry(
                $"Variables[{name}]", JsonSerializer.Serialize( fromMap[name] ), null, DiffChangeType.Removed ) );
        }

        foreach (string name in toMap.Keys.Except( fromMap.Keys, StringComparer.OrdinalIgnoreCase )) {
            entries.Add( new WorkflowVersionDiffEntry(
                $"Variables[{name}]", null, JsonSerializer.Serialize( toMap[name] ), DiffChangeType.Added ) );
        }

        foreach (string name in fromMap.Keys.Intersect( toMap.Keys, StringComparer.OrdinalIgnoreCase )) {
            string oldJson = JsonSerializer.Serialize( fromMap[name] );
            string newJson = JsonSerializer.Serialize( toMap[name] );
            if (!string.Equals( oldJson, newJson, StringComparison.Ordinal )) {
                entries.Add( new WorkflowVersionDiffEntry(
                    $"Variables[{name}]", oldJson, newJson, DiffChangeType.Modified ) );
            }
        }
    }

    private static void CompareScalar( List<WorkflowVersionDiffEntry> entries, string path, string? oldVal, string? newVal ) {
        if (string.Equals( oldVal, newVal, StringComparison.Ordinal )) {
            return;
        }

        DiffChangeType changeType = (oldVal, newVal) switch {
            (null or "", not null and not "") => DiffChangeType.Added,
            (not null and not "", null or "") => DiffChangeType.Removed,
            _ => DiffChangeType.Modified,
        };

        entries.Add( new WorkflowVersionDiffEntry( path, oldVal, newVal, changeType ) );
    }

    private static void CompareArray( List<WorkflowVersionDiffEntry> entries, string path, string[]? oldArr, string[]? newArr ) {
        string oldJson = NormalizeArray( oldArr );
        string newJson = NormalizeArray( newArr );

        if (string.Equals( oldJson, newJson, StringComparison.Ordinal )) {
            return;
        }

        DiffChangeType changeType = (oldArr, newArr) switch {
            (null, not null) => DiffChangeType.Added,
            (not null, null) => DiffChangeType.Removed,
            _ => DiffChangeType.Modified,
        };

        entries.Add( new WorkflowVersionDiffEntry( path, oldJson, newJson, changeType ) );
    }

    private static void CompareJson( List<WorkflowVersionDiffEntry> entries, string path, string? oldJson, string? newJson ) {
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

        entries.Add( new WorkflowVersionDiffEntry( path, normalizedOld, normalizedNew, changeType ) );
    }

    private static string NormalizeArray( string[]? arr ) {
        if (arr is null || arr.Length == 0) {
            return "[]";
        }
        string[] sorted = [.. arr.OrderBy( s => s, StringComparer.Ordinal )];
        return JsonSerializer.Serialize( sorted );
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
