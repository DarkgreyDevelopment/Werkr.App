namespace Werkr.Common.Models;

/// <summary>Response DTO for a step dependency.</summary>
public sealed record StepDependencyDto( long StepId, long DependsOnStepId );
