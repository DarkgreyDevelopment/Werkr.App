namespace Werkr.Common.Models;

/// <summary>Request DTO for manually editing a runtime variable value (creates a new version).</summary>
public sealed record EditVariableRequest(
    string Value
);
