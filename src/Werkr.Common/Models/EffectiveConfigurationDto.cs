namespace Werkr.Common.Models;

/// <summary>
/// Resolved effective configuration value with source attribution.
/// </summary>
public sealed record EffectiveConfigurationDto(
    string Key,
    string EffectiveValue,
    string Source,
    string GlobalValue,
    string? OverrideValue,
    string? AgentId,
    string ValueType,
    string Category,
    string? Description,
    string? ValidationRules
);
