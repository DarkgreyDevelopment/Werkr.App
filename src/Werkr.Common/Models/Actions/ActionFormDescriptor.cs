namespace Werkr.Common.Models.Actions;

/// <summary>Describes a single built-in action, its expected parameter fields, and its category.</summary>
/// <param name="Key">Action key string (e.g. "CopyFile") stored in ActionSubType.</param>
/// <param name="DisplayName">Human-friendly name shown in the dropdown.</param>
/// <param name="Description">Short description of what the action does.</param>
/// <param name="Category">Grouping category for the action dropdown (e.g. "File", "Network").</param>
/// <param name="ParameterType">The CLR type of the parameter record for API-side validation.</param>
/// <param name="Fields">Ordered list of form fields.</param>
public sealed record ActionFormDescriptor(
    string Key,
    string DisplayName,
    string Description,
    string Category,
    Type ParameterType,
    IReadOnlyList<FieldDescriptor> Fields
);
