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

    /// <summary>PathType values for TestExists and ListDirectory (matches Werkr.Common.Models.PathType enum).</summary>
    private static readonly string[] s_pathTypes = ["File", "Directory", "Any"];

    /// <summary>WatchFileMode values (matches Werkr.Common.Models.WatchFileMode enum).</summary>
    private static readonly string[] s_watchFileModes = ["FailOnTimeout", "ExitQuietly"];

    /// <summary>Archive format values for CompressArchive (Auto excluded — handler rejects it for compression).</summary>
    private static readonly string[] s_archiveFormats = ["Zip", "TarGz"];

    /// <summary>Archive format values for ExpandArchive (includes Auto for extension-based detection).</summary>
    private static readonly string[] s_expandArchiveFormats = ["Zip", "TarGz", "Auto"];

    /// <summary>Compression level values (matches Werkr.Common.Models.ArchiveCompressionLevel enum).</summary>
    private static readonly string[] s_compressionLevels = ["Fastest", "Optimal", "SmallestSize"];

    /// <summary>Sort-by values for ListDirectory (matches Werkr.Common.Models.DirectoryListSortBy enum).</summary>
    private static readonly string[] s_directoryListSortBy = ["Name", "Modified", "Size", "None"];

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

        // ── Control operations ───────────────────────────────────────
        new( "Delay", "Delay", "Pause workflow execution for a specified duration.", [
            new( "Seconds", "Seconds", FieldType.Number, Required: true, Placeholder: "10", HelpText: "Duration to pause in seconds." ),
            new( "Reason", "Reason", FieldType.Text, Placeholder: "Wait for external system to settle…" ),
        ] ),

        // ── File information ─────────────────────────────────────────
        new( "GetFileInfo", "Get File Info", "Return file or directory metadata as JSON.", [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\file.txt" ),
        ] ),

        new( "ReadContent", "Read Content", "Read file content and emit it as action output.", [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\file.txt" ),
            new( "Encoding", "Encoding", FieldType.Select, DefaultValue: "utf-8", Options: Encodings ),
            new( "MaxBytes", "Max Bytes", FieldType.Number, HelpText: "Leave blank for no limit." ),
        ] ),

        new( "ListDirectory", "List Directory", "Enumerate files or directories matching a pattern.", [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\directory" ),
            new( "Pattern", "Pattern", FieldType.Text, DefaultValue: "*", Placeholder: "*.csv", HelpText: "Glob pattern for matching entries." ),
            new( "Recursive", "Recursive", FieldType.Bool, DefaultValue: "false" ),
            new( "Type", "Entry Type", FieldType.Select, DefaultValue: "File", Options: s_pathTypes, HelpText: "File, Directory, or Any." ),
            new( "SortBy", "Sort By", FieldType.Select, DefaultValue: "Name", Options: s_directoryListSortBy ),
        ] ),

        new( "FindReplace", "Find & Replace", "Perform string or regex find-and-replace within a file.", [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\file.txt" ),
            new( "Find", "Find", FieldType.Text, Required: true, Placeholder: "search-text" ),
            new( "Replace", "Replace", FieldType.Text, Required: true, Placeholder: "replacement-text" ),
            new( "IsRegex", "Use Regex", FieldType.Bool, DefaultValue: "false" ),
            new( "CaseSensitive", "Case Sensitive", FieldType.Bool, DefaultValue: "true" ),
            new( "Encoding", "Encoding", FieldType.Select, DefaultValue: "utf-8", Options: Encodings ),
        ] ),

        // ── Archive operations ───────────────────────────────────────
        new( "CompressArchive", "Compress Archive", "Create a Zip or TarGz archive from a source path.", [
            new( "Source", "Source", FieldType.Text, Required: true, Placeholder: "C:\\source\\folder", HelpText: "Supports glob patterns." ),
            new( "Destination", "Destination", FieldType.Text, Required: true, Placeholder: "C:\\dest\\archive.zip" ),
            new( "Format", "Format", FieldType.Select, DefaultValue: "Zip", Options: s_archiveFormats ),
            new( "CompressionLevel", "Compression Level", FieldType.Select, DefaultValue: "Optimal", Options: s_compressionLevels ),
            new( "IncludeBaseDirectory", "Include Base Directory", FieldType.Bool, DefaultValue: "false" ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
        ] ),

        new( "ExpandArchive", "Expand Archive", "Extract a Zip or TarGz archive to a destination.", [
            new( "Source", "Source", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\archive.zip" ),
            new( "Destination", "Destination", FieldType.Text, Required: true, Placeholder: "C:\\dest\\folder" ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
            new( "Format", "Format", FieldType.Select, DefaultValue: "Auto", Options: s_expandArchiveFormats, HelpText: "Auto-detects format by file extension." ),
        ] ),

        // ── Event operations ─────────────────────────────────────────
        new( "WatchFile", "Watch File", "Monitor a directory for a file matching a glob pattern.", [
            new( "Directory", "Directory", FieldType.Text, Required: true, Placeholder: "C:\\watched\\folder" ),
            new( "Pattern", "Pattern", FieldType.Text, Required: true, Placeholder: "*.csv", HelpText: "Glob pattern for matching files." ),
            new( "StabilitySeconds", "Stability (seconds)", FieldType.Number, DefaultValue: "5", HelpText: "Time the file size must remain stable before match." ),
            new( "TimeoutSeconds", "Timeout (seconds)", FieldType.Number, DefaultValue: "300" ),
            new( "PollIntervalMs", "Poll Interval (ms)", FieldType.Number, DefaultValue: "1000" ),
            new( "Mode", "Timeout Mode", FieldType.Select, DefaultValue: "FailOnTimeout", Options: s_watchFileModes, HelpText: "FailOnTimeout fails the action; ExitQuietly succeeds with a 'not found' output." ),
            new( "UsePolling", "Use Polling", FieldType.Bool, DefaultValue: "false", HelpText: "Recommended for network or UNC paths." ),
        ] ),
    ];

    /// <summary>Fast lookup by action key (case-insensitive).</summary>
    public static readonly FrozenDictionary<string, ActionFormDescriptor> Actions =
        s_allDescriptors.ToFrozenDictionary( d => d.Key, StringComparer.OrdinalIgnoreCase );

    /// <summary>Ordered list of all descriptors for populating dropdowns.</summary>
    public static IReadOnlyList<ActionFormDescriptor> All => s_allDescriptors;
}
