using System.Collections.Frozen;
using Werkr.Server.Components.Shared;

namespace Werkr.Server.Services;

/// <summary>
/// Registry of all built-in action form descriptors.
/// Used by the <see cref="ActionParameterEditor"/> component to render dynamic forms.
/// </summary>
public static class ActionParameterRegistry {
    /// <summary>Encoding values matching the PowerShell Out-File parameter set.</summary>
    public static readonly string[] Encodings = [
        "ascii", "utf-8", "utf-8-bom", "utf-16", "utf-16BE",
        "utf-32", "utf-32BE", "utf-7", "oem"
    ];

    /// <summary>PathType values for TestExists (matches Werkr.Common.Models.PathType enum).</summary>
    private static readonly string[] s_pathTypes = ["File", "Directory", "Any"];

    /// <summary>
    /// The master array of all <see cref="ActionFormDescriptor"/> instances that define every supported action and its parameters. This array is the source of truth from which <see cref="Actions"/> and <see cref="All"/> are derived.
    /// </summary>
    private static readonly ActionFormDescriptor[] s_allDescriptors = [
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
            new( "Type", "Path Type", FieldType.Select, DefaultValue: "Any", Options: s_pathTypes, HelpText: "File, Directory, or Any." ),
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
        s_allDescriptors.ToFrozenDictionary( d => d.Key, StringComparer.OrdinalIgnoreCase );

    /// <summary>Ordered list of all descriptors for populating dropdowns.</summary>
    public static IReadOnlyList<ActionFormDescriptor> All => s_allDescriptors;
}
