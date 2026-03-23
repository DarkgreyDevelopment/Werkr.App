namespace Werkr.Common.Models;

/// <summary>Request DTO for associating a schedule with a workflow.</summary>
public sealed record WorkflowScheduleAssociateRequest(
    Guid ScheduleId
);
