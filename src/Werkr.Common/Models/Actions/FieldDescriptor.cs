namespace Werkr.Common.Models.Actions;

/// <summary>Describes a single form field for an action parameter.</summary>
/// <param name="Name">JSON property name (PascalCase) written into the parameters blob.</param>
/// <param name="Label">Human-friendly label shown in the form.</param>
/// <param name="Type">Control type to render.</param>
/// <param name="Required">Whether the field must have a value.</param>
/// <param name="DefaultValue">Default value as a string (bool → "false", int → "0", etc.).</param>
/// <param name="Placeholder">Optional placeholder text.</param>
/// <param name="Options">For <see cref="FieldType.Select"/> — the allowed option values.</param>
/// <param name="HelpText">Tooltip or small help text shown below the control.</param>
/// <param name="ShowWhen">Conditional visibility expression: "FieldName=value" or "FieldName=a|b".
/// When set, the field is only visible when the referenced field has one of the specified values.
/// Null means always visible.</param>
/// <param name="Min">Minimum value for <see cref="FieldType.Number"/> fields. Null means no minimum.</param>
/// <param name="Max">Maximum value for <see cref="FieldType.Number"/> fields. Null means no maximum.</param>
/// <param name="SubFields">For <see cref="FieldType.ObjectArray"/> — the field descriptors for each object in the array.</param>
public sealed record FieldDescriptor(
    string Name,
    string Label,
    FieldType Type,
    bool Required = false,
    string? DefaultValue = null,
    string? Placeholder = null,
    string[]? Options = null,
    string? HelpText = null,
    string? ShowWhen = null,
    double? Min = null,
    double? Max = null,
    IReadOnlyList<FieldDescriptor>? SubFields = null
);
