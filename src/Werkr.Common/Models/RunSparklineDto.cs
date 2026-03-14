namespace Werkr.Common.Models;

/// <summary>Minimal run data for sparkline visualization.</summary>
public sealed record RunSparklineDto(
    Guid RunId,
    string Status,
    double? DurationSeconds
);
