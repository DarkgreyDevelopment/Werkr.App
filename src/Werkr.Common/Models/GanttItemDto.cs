namespace Werkr.Common.Models;

/// <summary>DTO representing a single step execution bar for the Gantt timeline view.</summary>
public sealed record GanttItemDto(
    string Id,
    string Content,
    string Start,
    string? End,
    string ClassName,
    string Title
);
