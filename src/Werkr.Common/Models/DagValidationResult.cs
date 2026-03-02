namespace Werkr.Common.Models;

/// <summary>Result of DAG validation for a workflow.</summary>
public sealed record DagValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors );
