namespace Werkr.Common.Models;

/// <summary>Request DTO for updating an existing workflow variable definition.</summary>
public sealed record UpdateVariableRequest(
    string? Name = null,
    string? Description = null,
    string? DefaultValue = null,
    string? DataType = null,
    bool? IsRequired = null,
    bool? LogRedaction = null
);
