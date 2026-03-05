namespace Werkr.Server.Services;

/// <summary>Describes a single built-in action and its expected parameter fields.</summary>
/// <param name="Key">Action key string (e.g. "CopyFile") stored in ActionSubType.</param>
/// <param name="DisplayName">Human-friendly name shown in the dropdown.</param>
/// <param name="Description">Short description of what the action does.</param>
/// <param name="Fields">Ordered list of form fields.</param>
public sealed record ActionFormDescriptor(
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<FieldDescriptor> Fields
);
