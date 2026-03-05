namespace Werkr.Common.Models;

/// <summary>Response body from a server URL change notification operation.</summary>
public sealed record NotifyUrlChangeResponse(
    int Notified,
    int Failed,
    IReadOnlyList<string> FailedAgents
);
