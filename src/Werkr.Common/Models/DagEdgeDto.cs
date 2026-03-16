namespace Werkr.Common.Models;

/// <summary>Edge data for X6 DAG rendering (dependency link).</summary>
public sealed record DagEdgeDto(
    long SourceStepId,
    long TargetStepId
);
