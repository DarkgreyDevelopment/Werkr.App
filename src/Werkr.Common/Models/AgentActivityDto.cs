namespace Werkr.Common.Models;

/// <summary>Agent activity timeline entry.</summary>
public sealed record AgentActivityDto(
    Guid AgentId,
    string ConnectionName,
    string EventType,
    DateTime OccurredAtUtc,
    string Status
);
