namespace Werkr.Common.Models;

/// <summary>Request DTO for updating an existing workflow.</summary>
public sealed record WorkflowUpdateRequest(
    string Name,
    string? Description = null,
    bool Enabled = true,
    string[]? TargetTags = null,
    List<AnnotationDto>? Annotations = null,
    int? ExpectedVersionNumber = null,
    string? ChangeDescription = null
);
