
using System.Collections.Frozen;

namespace Werkr.Server.Services;
/// <summary>Describes the field type rendered in the parameter editor.</summary>
public enum FieldType {
    /// <summary>Single-line text input.</summary>
    Text,
    /// <summary>Multi-line text area.</summary>
    TextArea,
    /// <summary>Numeric input.</summary>
    Number,
    /// <summary>Boolean toggle / checkbox.</summary>
    Bool,
    /// <summary>Dropdown select from a fixed set of options.</summary>
    Select
}

/// <summary>Describes a single form field for an action parameter.</summary>
/// <param name="Name">JSON property name (PascalCase) written into the parameters blob.</param>
/// <param name="Label">Human-friendly label shown in the form.</param>
/// <param name="Type">Control type to render.</param>
/// <param name="Required">Whether the field must have a value.</param>
/// <param name="DefaultValue">Default value as a string (bool → "false", int → "0", etc.).</param>
/// <param name="Placeholder">Optional placeholder text.</param>
/// <param name="Options">For <see cref="FieldType.Select"/> — the allowed option values.</param>
/// <param name="HelpText">Tooltip or small help text shown below the control.</param>
public sealed record FieldDescriptor(
    string Name,
    string Label,
    FieldType Type,
    bool Required = false,
    string? DefaultValue = null,
    string? Placeholder = null,
    string[]? Options = null,
    string? HelpText = null );

/// <summary>Describes a single built-in action and its expected parameter fields.</summary>
/// <param name="Key">Action key string (e.g. "CopyFile") stored in ActionSubType.</param>
/// <param name="DisplayName">Human-friendly name shown in the dropdown.</param>
/// <param name="Description">Short description of what the action does.</param>
/// <param name="Fields">Ordered list of form fields.</param>
public sealed record ActionFormDescriptor(
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<FieldDescriptor> Fields );

/// <summary>
/// Registry of all built-in action form descriptors.
/// Used by the <c>ActionParameterEditor</c> component to render dynamic forms.
/// </summary>
public static class ActionParameterRegistry {
    /// <summary>Encoding values matching the PowerShell Out-File parameter set.</summary>
    public static readonly string[] Encodings = [
        "ascii", "utf-8", "utf-8-bom", "utf-16", "utf-16BE",
        "utf-32", "utf-32BE", "utf-7", "oem"
    ];

    /// <summary>PathType values for TestExists (matches Werkr.Common.Models.PathType enum).</summary>
    private static readonly string[] PathTypes = ["File", "Directory", "Any"];

    private static readonly ActionFormDescriptor[] AllDescriptors = [
        // ── File operations ──────────────────────────────────────────
        new( "CopyFile", "Copy File", "Copy a file or directory to a new location.", [
            new( "Source", "Source Path", FieldType.Text, Required: true, Placeholder: "C:\\source\\file.txt", HelpText: "Supports wildcard patterns." ),
            new( "Destination", "Destination Path", FieldType.Text, Required: true, Placeholder: "C:\\dest\\file.txt" ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
            new( "Recursive", "Recursive", FieldType.Bool, DefaultValue: "false", HelpText: "Copy directories recursively." ),
        ] ),

        new( "MoveFile", "Move File", "Move a file or directory to a new location.", [
            new( "Source", "Source Path", FieldType.Text, Required: true, Placeholder: "C:\\source\\file.txt", HelpText: "Supports wildcard patterns." ),
            new( "Destination", "Destination Path", FieldType.Text, Required: true, Placeholder: "C:\\dest\\file.txt" ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
        ] ),

        new( "RenameFile", "Rename File", "Rename a file or directory.", [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\item" ),
            new( "NewName", "New Name", FieldType.Text, Required: true, Placeholder: "new-name.txt", HelpText: "Just the name, not a full path." ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
        ] ),

        new( "DeleteFile", "Delete File", "Delete a file or directory.", [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\item" ),
            new( "Recursive", "Recursive", FieldType.Bool, DefaultValue: "false" ),
            new( "Force", "Force", FieldType.Bool, DefaultValue: "false", HelpText: "Remove read-only attributes before deletion." ),
        ] ),

        new( "CreateFile", "Create File", "Create a new file with optional content.", [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\file.txt" ),
            new( "Content", "Content", FieldType.TextArea, Placeholder: "File content…" ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
            new( "Encoding", "Encoding", FieldType.Select, DefaultValue: "utf-8", Options: Encodings ),
            new( "CreateParentDirectories", "Create Parent Dirs", FieldType.Bool, DefaultValue: "true" ),
        ] ),

        new( "CreateDirectory", "Create Directory", "Create one or more directories.", [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\directory" ),
        ] ),

        new( "TestExists", "Test Exists", "Check if a path exists.", [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\check" ),
            new( "Type", "Path Type", FieldType.Select, DefaultValue: "Any", Options: PathTypes, HelpText: "File, Directory, or Any." ),
        ] ),

        // ── Content operations ───────────────────────────────────────
        new( "ClearContent", "Clear Content", "Truncate a file to zero bytes.", [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\file.txt" ),
        ] ),

        new( "WriteContent", "Write Content", "Write or append text to a file.", [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\file.txt" ),
            new( "Content", "Content", FieldType.TextArea, Required: true, Placeholder: "Text to write…" ),
            new( "Append", "Append", FieldType.Bool, DefaultValue: "false", HelpText: "Append instead of overwriting." ),
            new( "Encoding", "Encoding", FieldType.Select, DefaultValue: "utf-8", Options: Encodings ),
        ] ),

        // ── Process operations ───────────────────────────────────────
        new( "StartProcess", "Start Process", "Launch a process.", [
            new( "FileName", "File Name", FieldType.Text, Required: true, Placeholder: "notepad.exe" ),
            new( "Arguments", "Arguments", FieldType.Text, Placeholder: "--flag value" ),
            new( "WorkingDirectory", "Working Directory", FieldType.Text, Placeholder: "C:\\work" ),
            new( "WaitForExit", "Wait for Exit", FieldType.Bool, DefaultValue: "false" ),
            new( "TimeoutMs", "Timeout (ms)", FieldType.Number, HelpText: "Only used when Wait for Exit is true." ),
        ] ),

        new( "StopProcess", "Stop Process", "Stop a running process.", [
            new( "ProcessName", "Process Name", FieldType.Text, Required: true, Placeholder: "notepad" ),
            new( "ProcessId", "Process ID", FieldType.Number, HelpText: "Optional — when set only this PID is stopped." ),
            new( "Force", "Force", FieldType.Bool, DefaultValue: "false", HelpText: "Forcefully terminate the process." ),
        ] ),
    ];

    /// <summary>Fast lookup by action key (case-insensitive).</summary>
    public static readonly FrozenDictionary<string, ActionFormDescriptor> Actions =
        AllDescriptors.ToFrozenDictionary( d => d.Key, StringComparer.OrdinalIgnoreCase );

    /// <summary>Ordered list of all descriptors for populating dropdowns.</summary>
    public static IReadOnlyList<ActionFormDescriptor> All => AllDescriptors;
}
