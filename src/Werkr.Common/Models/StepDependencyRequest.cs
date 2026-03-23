namespace Werkr.Common.Models;

/// <summary>Request DTO for creating a step dependency.</summary>
public sealed record StepDependencyRequest( long DependsOnStepId );
