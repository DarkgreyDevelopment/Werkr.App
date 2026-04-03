using System.Collections.Frozen;

namespace Werkr.Common.Models.Actions;

/// <summary>
/// Single source of truth for all built-in action metadata.
/// Referenced by the Server UI (form rendering), API (parameter validation),
/// and can be consulted by the Agent (startup validation).
/// </summary>
public static class ActionRegistry {
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

    /// <summary>HTTP method values for HttpRequest and related actions.</summary>
    private static readonly string[] s_httpMethods = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"];

    /// <summary>Connection protocol values for TestConnection.</summary>
    private static readonly string[] s_connectionProtocols = ["Tcp", "Http", "Https", "Icmp"];

    /// <summary>Upload method values for UploadFile.</summary>
    private static readonly string[] s_uploadMethods = ["POST", "PUT"];

    /// <summary>JSON transform operation types for TransformJson.</summary>
    private static readonly string[] s_transformTypes = ["Extract", "Set", "Delete", "Merge"];

    /// <summary>
    /// The master array of all <see cref="ActionFormDescriptor"/> instances that define
    /// every supported action and its parameters. This array is the source of truth
    /// from which <see cref="Actions"/>, <see cref="All"/>, <see cref="Categories"/>,
    /// and <see cref="Grouped"/> are derived.
    /// </summary>
    private static readonly ActionFormDescriptor[] s_allDescriptors = [
        // ── File operations ──────────────────────────────────────────
        new( "CopyFile", "Copy File", "Copy a file or directory to a new location.",
            "File", typeof( CopyFileParameters ), [
            new( "Source", "Source Path", FieldType.Text, Required: true, Placeholder: "C:\\source\\file.txt", HelpText: "Supports wildcard patterns." ),
            new( "Destination", "Destination Path", FieldType.Text, Required: true, Placeholder: "C:\\dest\\file.txt" ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
            new( "Recursive", "Recursive", FieldType.Bool, DefaultValue: "false", HelpText: "Copy directories recursively." ),
        ] ),

        new( "MoveFile", "Move File", "Move a file or directory to a new location.",
            "File", typeof( MoveFileParameters ), [
            new( "Source", "Source Path", FieldType.Text, Required: true, Placeholder: "C:\\source\\file.txt", HelpText: "Supports wildcard patterns." ),
            new( "Destination", "Destination Path", FieldType.Text, Required: true, Placeholder: "C:\\dest\\file.txt" ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
        ] ),

        new( "RenameFile", "Rename File", "Rename a file or directory.",
            "File", typeof( RenameFileParameters ), [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\item" ),
            new( "NewName", "New Name", FieldType.Text, Required: true, Placeholder: "new-name.txt", HelpText: "Just the name, not a full path." ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
        ] ),

        new( "DeleteFile", "Delete File", "Delete a file or directory.",
            "File", typeof( DeleteFileParameters ), [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\item" ),
            new( "Recursive", "Recursive", FieldType.Bool, DefaultValue: "false" ),
            new( "Force", "Force", FieldType.Bool, DefaultValue: "false", HelpText: "Remove read-only attributes before deletion." ),
        ] ),

        new( "CreateFile", "Create File", "Create a new file with optional content.",
            "File", typeof( CreateFileParameters ), [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\file.txt" ),
            new( "Content", "Content", FieldType.TextArea, Placeholder: "File content…" ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
            new( "Encoding", "Encoding", FieldType.Select, DefaultValue: "utf-8", Options: Encodings ),
            new( "CreateParentDirectories", "Create Parent Dirs", FieldType.Bool, DefaultValue: "true" ),
        ] ),

        new( "CreateDirectory", "Create Directory", "Create one or more directories.",
            "Directory", typeof( CreateDirectoryParameters ), [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\directory" ),
        ] ),

        new( "TestExists", "Test Exists", "Check if a path exists.",
            "File", typeof( TestExistsParameters ), [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\check" ),
            new( "Type", "Path Type", FieldType.Select, DefaultValue: "Any", Options: s_pathTypes, HelpText: "File, Directory, or Any." ),
        ] ),

        // ── Content operations ───────────────────────────────────────
        new( "ClearContent", "Clear Content", "Truncate a file to zero bytes.",
            "File", typeof( ClearContentParameters ), [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\file.txt" ),
        ] ),

        new( "WriteContent", "Write Content", "Write or append text to a file.",
            "File", typeof( WriteContentParameters ), [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\file.txt" ),
            new( "Content", "Content", FieldType.TextArea, Required: true, Placeholder: "Text to write…" ),
            new( "Append", "Append", FieldType.Bool, DefaultValue: "false", HelpText: "Append instead of overwriting." ),
            new( "Encoding", "Encoding", FieldType.Select, DefaultValue: "utf-8", Options: Encodings ),
        ] ),

        new( "ReadContent", "Read Content", "Read file content and emit it as action output.",
            "File", typeof( ReadContentParameters ), [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\file.txt" ),
            new( "Encoding", "Encoding", FieldType.Select, DefaultValue: "utf-8", Options: Encodings ),
            new( "MaxBytes", "Max Bytes", FieldType.Number, HelpText: "Leave blank for no limit." ),
        ] ),

        new( "FindReplace", "Find & Replace", "Perform string or regex find-and-replace within a file.",
            "File", typeof( FindReplaceParameters ), [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\file.txt" ),
            new( "Find", "Find", FieldType.Text, Required: true, Placeholder: "search-text" ),
            new( "Replace", "Replace", FieldType.Text, Required: true, Placeholder: "replacement-text" ),
            new( "IsRegex", "Use Regex", FieldType.Bool, DefaultValue: "false" ),
            new( "CaseSensitive", "Case Sensitive", FieldType.Bool, DefaultValue: "true" ),
            new( "Encoding", "Encoding", FieldType.Select, DefaultValue: "utf-8", Options: Encodings ),
        ] ),

        // ── File information ─────────────────────────────────────────
        new( "GetFileInfo", "Get File Info", "Return file or directory metadata as JSON.",
            "File", typeof( GetFileInfoParameters ), [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\file.txt" ),
        ] ),

        new( "ListDirectory", "List Directory", "Enumerate files or directories matching a pattern.",
            "Directory", typeof( ListDirectoryParameters ), [
            new( "Path", "Path", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\directory" ),
            new( "Pattern", "Pattern", FieldType.Text, DefaultValue: "*", Placeholder: "*.csv", HelpText: "Glob pattern for matching entries." ),
            new( "Recursive", "Recursive", FieldType.Bool, DefaultValue: "false" ),
            new( "Type", "Entry Type", FieldType.Select, DefaultValue: "File", Options: s_pathTypes, HelpText: "File, Directory, or Any." ),
            new( "SortBy", "Sort By", FieldType.Select, DefaultValue: "Name", Options: s_directoryListSortBy ),
        ] ),

        // ── Archive operations ───────────────────────────────────────
        new( "CompressArchive", "Compress Archive", "Create a Zip or TarGz archive from a source path.",
            "Archive", typeof( CompressArchiveParameters ), [
            new( "Source", "Source", FieldType.Text, Required: true, Placeholder: "C:\\source\\folder", HelpText: "Supports glob patterns." ),
            new( "Destination", "Destination", FieldType.Text, Required: true, Placeholder: "C:\\dest\\archive.zip" ),
            new( "Format", "Format", FieldType.Select, DefaultValue: "Zip", Options: s_archiveFormats ),
            new( "CompressionLevel", "Compression Level", FieldType.Select, DefaultValue: "Optimal", Options: s_compressionLevels ),
            new( "IncludeBaseDirectory", "Include Base Directory", FieldType.Bool, DefaultValue: "false" ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
        ] ),

        new( "ExpandArchive", "Expand Archive", "Extract a Zip or TarGz archive to a destination.",
            "Archive", typeof( ExpandArchiveParameters ), [
            new( "Source", "Source", FieldType.Text, Required: true, Placeholder: "C:\\path\\to\\archive.zip" ),
            new( "Destination", "Destination", FieldType.Text, Required: true, Placeholder: "C:\\dest\\folder" ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
            new( "Format", "Format", FieldType.Select, DefaultValue: "Auto", Options: s_expandArchiveFormats, HelpText: "Auto-detects format by file extension." ),
        ] ),

        // ── Process operations ───────────────────────────────────────
        new( "StartProcess", "Start Process", "Launch a process.",
            "Process", typeof( StartProcessParameters ), [
            new( "FileName", "File Name", FieldType.Text, Required: true, Placeholder: "notepad.exe" ),
            new( "Arguments", "Arguments", FieldType.Text, Placeholder: "--flag value" ),
            new( "WorkingDirectory", "Working Directory", FieldType.Text, Placeholder: "C:\\work" ),
            new( "WaitForExit", "Wait for Exit", FieldType.Bool, DefaultValue: "false" ),
            new( "TimeoutMs", "Timeout (ms)", FieldType.Number, ShowWhen: "WaitForExit=true", HelpText: "Only used when Wait for Exit is true." ),
        ] ),

        new( "StopProcess", "Stop Process", "Stop a running process.",
            "Process", typeof( StopProcessParameters ), [
            new( "ProcessName", "Process Name", FieldType.Text, Required: true, Placeholder: "notepad" ),
            new( "ProcessId", "Process ID", FieldType.Number, HelpText: "Optional — when set only this PID is stopped." ),
            new( "Force", "Force", FieldType.Bool, DefaultValue: "false", HelpText: "Forcefully terminate the process." ),
        ] ),

        // ── ControlFlow operations ───────────────────────────────────
        new( "Delay", "Delay", "Pause workflow execution for a specified duration.",
            "ControlFlow", typeof( DelayParameters ), [
            new( "Seconds", "Seconds", FieldType.Number, Required: true, Placeholder: "10", HelpText: "Duration to pause in seconds." ),
            new( "Reason", "Reason", FieldType.Text, Placeholder: "Wait for external system to settle…" ),
        ] ),

        // ── File monitoring operations ───────────────────────────────
        new( "WatchFile", "Watch File", "Monitor a directory for a file matching a glob pattern.",
            "File monitoring", typeof( WatchFileParameters ), [
            new( "Directory", "Directory", FieldType.Text, Required: true, Placeholder: "C:\\watched\\folder" ),
            new( "Pattern", "Pattern", FieldType.Text, Required: true, Placeholder: "*.csv", HelpText: "Glob pattern for matching files." ),
            new( "StabilitySeconds", "Stability (seconds)", FieldType.Number, DefaultValue: "5", HelpText: "Time the file size must remain stable before match." ),
            new( "TimeoutSeconds", "Timeout (seconds)", FieldType.Number, DefaultValue: "300" ),
            new( "PollIntervalMs", "Poll Interval (ms)", FieldType.Number, DefaultValue: "1000" ),
            new( "Mode", "Timeout Mode", FieldType.Select, DefaultValue: "FailOnTimeout", Options: s_watchFileModes, HelpText: "FailOnTimeout fails the action; ExitQuietly succeeds with a 'not found' output." ),
            new( "UsePolling", "Use Polling", FieldType.Bool, DefaultValue: "false", HelpText: "Recommended for network or UNC paths." ),
        ] ),


        // ── Network operations ───────────────────────────────────────
        new( "HttpRequest", "HTTP Request", "Send an HTTP request and capture the response.",
            "Network", typeof( HttpRequestParameters ), [
            new( "Url", "URL", FieldType.Text, Required: true, Placeholder: "https://api.example.com/data" ),
            new( "Method", "Method", FieldType.Select, DefaultValue: "GET", Options: s_httpMethods ),
            new( "Headers", "Headers", FieldType.KeyValueMap,
                 HelpText: "Request headers as key-value pairs." ),
            new( "Body", "Request Body", FieldType.TextArea, Placeholder: "{ \"key\": \"value\" }",
                 ShowWhen: "Method=POST|PUT|PATCH|DELETE",
                 HelpText: "Request body. If empty, the step's input variable value is used instead." ),
            new( "ContentType", "Content Type", FieldType.Text, Placeholder: "application/json",
                 ShowWhen: "Method=POST|PUT|PATCH|DELETE" ),
            new( "TimeoutSeconds", "Timeout (seconds)", FieldType.Number, DefaultValue: "30", Min: 1 ),
            new( "ExpectedStatusCodes", "Expected Status Codes", FieldType.IntArray, DefaultValue: "[200]",
                 HelpText: "Status codes that indicate success. Action fails if response code is not in this list." ),
            new( "OutputFilePath", "Output File Path", FieldType.Text, Placeholder: "/path/to/response.json",
                 HelpText: "Save full response body to this file instead of including it in the output variable." ),
            new( "FollowRedirects", "Follow Redirects", FieldType.Bool, DefaultValue: "false" ),
            new( "AuthType", "Auth Type", FieldType.Select, Options: ["", "basic", "bearer", "apikey"],
                 HelpText: "Authentication method. Leave empty for no auth." ),
            new( "AuthUsername", "Auth Username", FieldType.Text,
                 ShowWhen: "AuthType=basic",
                 HelpText: "Username for Basic authentication." ),
            new( "AuthCredential", "Auth Credential", FieldType.Text,
                 ShowWhen: "AuthType=basic|bearer|apikey",
                 HelpText: "Password (Basic), token (Bearer), or key value (API Key)." ),
            new( "AuthHeaderName", "Auth Header Name", FieldType.Text,
                 ShowWhen: "AuthType=apikey", DefaultValue: "X-Api-Key",
                 HelpText: "Header name for API Key authentication." ),
        ] ),

        new( "DownloadFile", "Download File", "Download a file from a URL to a local path.",
            "Network", typeof( DownloadFileParameters ), [
            new( "Url", "URL", FieldType.Text, Required: true, Placeholder: "https://example.com/file.zip" ),
            new( "Destination", "Destination Path", FieldType.Text, Required: true, Placeholder: "/downloads/file.zip" ),
            new( "Headers", "Headers", FieldType.KeyValueMap,
                 HelpText: "Optional request headers (e.g. Authorization)." ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
            new( "TimeoutSeconds", "Timeout (seconds)", FieldType.Number, DefaultValue: "300", Min: 1 ),
        ] ),

        new( "TestConnection", "Test Connection", "Verify TCP, HTTP(S), or ICMP connectivity.",
            "Network", typeof( TestConnectionParameters ), [
            new( "Host", "Host", FieldType.Text, Required: true, Placeholder: "example.com" ),
            new( "Port", "Port", FieldType.Number, Placeholder: "443", Min: 1, Max: 65535,
                 ShowWhen: "Protocol=Tcp|Http|Https",
                 HelpText: "Required for TCP/HTTP/HTTPS. Ignored for ICMP." ),
            new( "Protocol", "Protocol", FieldType.Select, DefaultValue: "Tcp", Options: s_connectionProtocols ),
            new( "TimeoutSeconds", "Timeout (seconds)", FieldType.Number, DefaultValue: "10", Min: 1 ),
            new( "ExpectedStatusCode", "Expected Status Code", FieldType.Number,
                 ShowWhen: "Protocol=Http|Https",
                 Placeholder: "200", HelpText: "Only for HTTP/HTTPS. If set and status doesn't match, reports unreachable." ),
        ] ),

        new( "UploadFile", "Upload File", "Upload a file to an HTTP endpoint via multipart form.",
            "Network", typeof( UploadFileParameters ), [
            new( "FilePath", "File Path", FieldType.Text, Required: true, Placeholder: "/path/to/file.pdf" ),
            new( "Url", "URL", FieldType.Text, Required: true, Placeholder: "https://api.example.com/upload" ),
            new( "Method", "HTTP Method", FieldType.Select, DefaultValue: "POST", Options: s_uploadMethods ),
            new( "FormFieldName", "Form Field Name", FieldType.Text, DefaultValue: "file",
                 HelpText: "The multipart form field name for the file." ),
            new( "Headers", "Headers", FieldType.KeyValueMap ),
            new( "TimeoutSeconds", "Timeout (seconds)", FieldType.Number, DefaultValue: "300", Min: 1 ),
        ] ),

        // ── Notification operations ──────────────────────────────────
        new( "SendEmail", "Send Email", "Send an email via SMTP.",
            "Network", typeof( SendEmailParameters ), [
            new( "SmtpHost", "SMTP Host", FieldType.Text, Required: true, Placeholder: "smtp.example.com" ),
            new( "Port", "Port", FieldType.Number, DefaultValue: "587", Min: 1, Max: 65535 ),
            new( "UseSsl", "Use SSL/TLS", FieldType.Bool, DefaultValue: "true" ),
            new( "CredentialName", "Credential Name", FieldType.Text,
                 Placeholder: "smtp-credentials",
                 HelpText: "Name of the credential in the agent's secret store. Leave blank for no authentication." ),
            new( "From", "From Address", FieldType.Text, Required: true, Placeholder: "noreply@example.com" ),
            new( "To", "To Addresses", FieldType.StringArray, Required: true,
                 Placeholder: "user@example.com",
                 HelpText: "One or more recipient email addresses." ),
            new( "Cc", "CC Addresses", FieldType.StringArray, Placeholder: "cc@example.com" ),
            new( "Subject", "Subject", FieldType.Text, Required: true, Placeholder: "Workflow Notification" ),
            new( "Body", "Body", FieldType.TextArea,
                 Placeholder: "Email body text...",
                 HelpText: "If empty, the step's input variable value is used as the body." ),
            new( "IsHtml", "HTML Body", FieldType.Bool, DefaultValue: "false",
                 HelpText: "Send body as HTML instead of plain text." ),
            new( "Attachments", "Attachments", FieldType.StringArray,
                 Placeholder: "/path/to/file.pdf",
                 HelpText: "File paths to attach to the email." ),
        ] ),

        new( "SendWebhook", "Send Webhook", "Send an HTTP POST with a JSON payload.",
            "Network", typeof( SendWebhookParameters ), [
            new( "Url", "Webhook URL", FieldType.Text, Required: true,
                 Placeholder: "https://hooks.example.com/webhook" ),
            new( "Payload", "Payload", FieldType.TextArea,
                 Placeholder: "{ \"text\": \"Workflow completed\" }",
                 HelpText: "JSON payload. If empty, the step's input variable value is used instead." ),
            new( "Headers", "Headers", FieldType.KeyValueMap,
                 HelpText: "Optional request headers." ),
            new( "TimeoutSeconds", "Timeout (seconds)", FieldType.Number, DefaultValue: "30", Min: 1 ),
        ] ),

        // ── Data operations ──────────────────────────────────────────
        new( "TransformJson", "Transform JSON", "Apply extract, set, delete, or merge operations to a JSON document.",
            "Data", typeof( TransformJsonParameters ), [
            new( "InputPath", "Input File Path", FieldType.Text,
                 Placeholder: "/path/to/input.json",
                 HelpText: "Read JSON from this file. If empty, the step's input variable value is used." ),
            new( "OutputPath", "Output File Path", FieldType.Text,
                 Placeholder: "/path/to/output.json",
                 HelpText: "Write transformed JSON to this file. Optional — result is always in the output variable." ),
            new( "Operations", "Operations", FieldType.ObjectArray, Required: true,
                 HelpText: "Ordered list of transform operations. Each is applied to the output of the previous.",
                 SubFields: [
                     new( "Type", "Operation", FieldType.Select, Required: true,
                          Options: s_transformTypes,
                          HelpText: "Extract: pull a value out. Set: create/replace a value. Delete: remove a property. Merge: deep-merge an object." ),
                     new( "Path", "JSON Path", FieldType.Text, Required: true,
                          Placeholder: "/property/name",
                          HelpText: "JSON Pointer (RFC 6901) to the target. E.g. /name, /address/city, /items/0" ),
                     new( "Value", "Value", FieldType.TextArea,
                          Placeholder: "\"new value\" or { \"key\": 1 }",
                          ShowWhen: "Type=Set|Merge",
                          HelpText: "The JSON value to set or merge. Required for Set and Merge operations." ),
                 ] ),
        ] ),

        // ── Shell operations ─────────────────────────────────────────
        new( "ShellCommand", "Shell Command", "Execute a system shell command (bash/cmd).",
            "Shell", typeof( ShellCommandParameters ), [
            new( "Content", "Command", FieldType.TextArea, Required: true,
                 Placeholder: "echo 'Hello, World!'",
                 HelpText: "The shell command to execute." ),
            new( "TimeoutMinutes", "Timeout (minutes)", FieldType.Number, DefaultValue: "60", Min: 1,
                 HelpText: "Maximum execution time in minutes." ),
        ] ),

        new( "ShellScript", "Shell Script", "Execute a shell script file (bash/sh).",
            "Shell", typeof( ShellScriptParameters ), [
            new( "ScriptPath", "Script Path", FieldType.Text, Required: true,
                 Placeholder: "/path/to/script.sh",
                 HelpText: "Path to the shell script file." ),
            new( "Arguments", "Arguments", FieldType.Text,
                 Placeholder: "--flag value",
                 HelpText: "Optional arguments to pass to the script." ),
            new( "TimeoutMinutes", "Timeout (minutes)", FieldType.Number, DefaultValue: "60", Min: 1,
                 HelpText: "Maximum execution time in minutes." ),
        ] ),

        // ── PowerShell operations ────────────────────────────────────
        new( "PowerShellCommand", "PowerShell Command", "Execute an inline PowerShell command or script block.",
            "PowerShell", typeof( PowerShellCommandParameters ), [
            new( "Content", "Command", FieldType.TextArea, Required: true,
                 Placeholder: "Get-Process | Where-Object { $_.CPU -gt 100 }",
                 HelpText: "The PowerShell command or script block to execute." ),
            new( "TimeoutMinutes", "Timeout (minutes)", FieldType.Number, DefaultValue: "60", Min: 1,
                 HelpText: "Maximum execution time in minutes." ),
        ] ),

        new( "PowerShellScript", "PowerShell Script", "Execute a PowerShell script file (.ps1).",
            "PowerShell", typeof( PowerShellScriptParameters ), [
            new( "ScriptPath", "Script Path", FieldType.Text, Required: true,
                 Placeholder: "C:\\scripts\\deploy.ps1",
                 HelpText: "Path to the PowerShell script file." ),
            new( "Arguments", "Arguments", FieldType.Text,
                 Placeholder: "-Environment Production -Verbose",
                 HelpText: "Optional arguments to pass to the script." ),
            new( "TimeoutMinutes", "Timeout (minutes)", FieldType.Number, DefaultValue: "60", Min: 1,
                 HelpText: "Maximum execution time in minutes." ),
        ] ),
    ];

    /// <summary>Fast lookup by action key (case-insensitive).</summary>
    public static readonly FrozenDictionary<string, ActionFormDescriptor> Actions =
        s_allDescriptors.ToFrozenDictionary( d => d.Key, StringComparer.OrdinalIgnoreCase );

    /// <summary>Ordered list of all descriptors for populating dropdowns.</summary>
    public static IReadOnlyList<ActionFormDescriptor> All => s_allDescriptors;

    /// <summary>Distinct categories in display order (derived from the descriptor array order).</summary>
    public static IReadOnlyList<string> Categories => s_categories;

    /// <summary>Descriptors grouped by category, preserving category order.</summary>
    public static IReadOnlyList<(string Category, IReadOnlyList<ActionFormDescriptor> Actions)> Grouped => s_grouped;

    /// <summary>Category list derived from descriptor order (stable, no duplicates).</summary>
    private static readonly string[] s_categories = [.. s_allDescriptors
        .Select( d => d.Category )
        .Distinct( StringComparer.OrdinalIgnoreCase )];

    /// <summary>Descriptors grouped by category.</summary>
    private static readonly (string Category, IReadOnlyList<ActionFormDescriptor> Actions)[] s_grouped =
        [.. s_allDescriptors
            .GroupBy( d => d.Category, StringComparer.OrdinalIgnoreCase )
            .Select( g => (g.Key, (IReadOnlyList<ActionFormDescriptor>) [.. g]) )];
}
