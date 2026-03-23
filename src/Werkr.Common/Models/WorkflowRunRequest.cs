namespace Werkr.Common.Models;

/// <summary>Request DTO for triggering a workflow run.</summary>
public sealed record WorkflowRunRequest(
    Dictionary<string, string>? Variables = null
);
