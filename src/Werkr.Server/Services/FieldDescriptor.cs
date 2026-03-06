namespace Werkr.Server.Services;

/// <summary>Describes a single form field for an action parameter.</summary>
/// <param name="Name">JSON property name (PascalCase) written into the parameters blob.</param>
/// <param name="Label">Human-friendly label shown in the form.</param>
/// <param name="Type">Control type to render.</param>
/// <param name="Required">Whether the field must have a value.</param>
/// <param name="DefaultValue">Default value as a string (bool → "false", int → "0", etc.).</param>
/// <param name="Placeholder">Optional placeholder text.</param>
/// <param name="Options">For <see cref="FieldType.Select"/> - the allowed option values.</param>
/// <param name="HelpText">Tooltip or small help text shown below the control.</param>
public sealed record FieldDescriptor(
    string Name,
    string Label,
    FieldType Type,
    bool Required = false,
    string? DefaultValue = null,
    string? Placeholder = null,
    string[]? Options = null,
    string? HelpText = null
);
