namespace Werkr.Common.Models;

/// <summary>Node data for X6 DAG rendering.</summary>
public sealed record DagNodeDto(
    long StepId,
    string StepLabel,
    string TaskName,
    string ControlStatement,
    int Order
);
