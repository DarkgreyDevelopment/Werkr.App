namespace Werkr.Common.Models;

/// <summary>Paginated response for the workflow dashboard endpoint.</summary>
public sealed record WorkflowDashboardPageDto(
    IReadOnlyList<WorkflowDashboardDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);
