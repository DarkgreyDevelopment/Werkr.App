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

    /// <summary>HTTP method values for HttpRequest and UploadFile.</summary>
    private static readonly string[] s_httpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    /// <summary>Connection protocol values (matches Werkr.Common.Models.Actions.ConnectionProtocol enum).</summary>
    private static readonly string[] s_connectionProtocols = ["Tcp", "Http", "Https"];

    /// <summary>JSON transform operation types (matches Werkr.Common.Models.Actions.JsonTransformType enum).</summary>
    private static readonly string[] s_jsonTransformTypes = ["Extract", "Set", "Delete", "Merge"];

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

        // ── Control flow ─────────────────────────────────────────────
        new( "ForEach", "For Each", "Iterate over items in a JSON array from the input variable.", [
            new( "ArrayPropertyName", "Array Property Name", FieldType.Text, Required: true, Placeholder: "items", HelpText: "Property name within the input JSON object that contains the array to iterate over." ),
        ] ),

        // ── Data operations ──────────────────────────────────────────
        new( "TransformJson", "Transform JSON", "Apply an ordered sequence of JSON operations to an input document.", [
            new( "InputPath", "Input File Path", FieldType.Text, Placeholder: "C:\\data\\input.json", HelpText: "Optional file to read JSON from. Takes precedence over the input variable." ),
            new( "OutputPath", "Output File Path", FieldType.Text, Placeholder: "C:\\data\\output.json", HelpText: "Optional file to write the transformed JSON to." ),
            new( "Operations[0].Type", "Operation Type", FieldType.Select, Required: true, DefaultValue: "Extract", Options: s_jsonTransformTypes, HelpText: "Type of the first transformation operation." ),
            new( "Operations[0].Path", "JSON Pointer Path", FieldType.Text, Required: true, Placeholder: "/property/name", HelpText: "RFC 6901 JSON Pointer path (e.g. /address/city). The prefix '$.' is also accepted." ),
            new( "Operations[0].Value", "Value (JSON)", FieldType.TextArea, Placeholder: "\"value\" or {\"key\":\"val\"}", HelpText: "JSON value for Set/Merge operations. Ignored for Extract/Delete." ),
        ] ),

        // ── Network operations ───────────────────────────────────────
        new( "HttpRequest", "HTTP Request", "Send an HTTP request and capture the response. Requires network actions to be enabled.", [
            new( "Url", "URL", FieldType.Text, Required: true, Placeholder: "https://api.example.com/endpoint" ),
            new( "Method", "Method", FieldType.Select, DefaultValue: "GET", Options: s_httpMethods ),
            new( "Body", "Request Body", FieldType.TextArea, Placeholder: "{\"key\":\"value\"}", HelpText: "Optional body. When omitted the input variable value is used." ),
            new( "ContentType", "Content-Type", FieldType.Text, Placeholder: "application/json", HelpText: "Content-Type header for the request body." ),
            new( "TimeoutSeconds", "Timeout (seconds)", FieldType.Number, DefaultValue: "30" ),
            new( "ExpectedStatusCodes", "Expected Status Codes", FieldType.Text, DefaultValue: "200", Placeholder: "200,201", HelpText: "Comma-separated list of acceptable HTTP status codes." ),
            new( "OutputFilePath", "Output File Path", FieldType.Text, Placeholder: "C:\\output\\response.json", HelpText: "Optional file path to stream the response body to." ),
            new( "FollowRedirects", "Follow Redirects", FieldType.Bool, DefaultValue: "false" ),
        ] ),

        new( "DownloadFile", "Download File", "Download a file from a URL to a local path. Requires network actions to be enabled.", [
            new( "Url", "URL", FieldType.Text, Required: true, Placeholder: "https://example.com/file.zip" ),
            new( "Destination", "Destination Path", FieldType.Text, Required: true, Placeholder: "C:\\downloads\\file.zip" ),
            new( "Overwrite", "Overwrite", FieldType.Bool, DefaultValue: "false" ),
            new( "TimeoutSeconds", "Timeout (seconds)", FieldType.Number, DefaultValue: "300" ),
        ] ),

        new( "UploadFile", "Upload File", "Upload a local file to a URL using multipart/form-data. Requires network actions to be enabled.", [
            new( "FilePath", "File Path", FieldType.Text, Required: true, Placeholder: "C:\\uploads\\report.csv" ),
            new( "Url", "URL", FieldType.Text, Required: true, Placeholder: "https://api.example.com/upload" ),
            new( "Method", "Method", FieldType.Select, DefaultValue: "POST", Options: ["POST", "PUT"] ),
            new( "FormFieldName", "Form Field Name", FieldType.Text, DefaultValue: "file", HelpText: "The multipart form field name for the file." ),
            new( "TimeoutSeconds", "Timeout (seconds)", FieldType.Number, DefaultValue: "300" ),
        ] ),

        new( "TestConnection", "Test Connection", "Test reachability of a host/port via TCP, HTTP, or HTTPS. Always succeeds; result is captured as output.", [
            new( "Host", "Host", FieldType.Text, Required: true, Placeholder: "api.example.com" ),
            new( "Port", "Port", FieldType.Number, Required: true, Placeholder: "443" ),
            new( "Protocol", "Protocol", FieldType.Select, DefaultValue: "Tcp", Options: s_connectionProtocols ),
            new( "TimeoutSeconds", "Timeout (seconds)", FieldType.Number, DefaultValue: "10" ),
            new( "ExpectedStatusCode", "Expected Status Code", FieldType.Number, HelpText: "For HTTP/HTTPS only: expected response code. Leave blank to accept any 2xx/3xx." ),
        ] ),

        new( "SendWebhook", "Send Webhook", "Fire a JSON POST to a webhook URL. Requires network actions to be enabled.", [
            new( "Url", "Webhook URL", FieldType.Text, Required: true, Placeholder: "https://hooks.example.com/trigger" ),
            new( "Payload", "Payload (JSON)", FieldType.TextArea, Placeholder: "{\"event\":\"build-complete\"}", HelpText: "Optional JSON payload. When omitted the input variable value is used." ),
            new( "TimeoutSeconds", "Timeout (seconds)", FieldType.Number, DefaultValue: "30" ),
        ] ),

        new( "SendEmail", "Send Email", "Send an email via SMTP using MailKit. Requires network actions to be enabled.", [
            new( "SmtpHost", "SMTP Host", FieldType.Text, Required: true, Placeholder: "smtp.example.com" ),
            new( "Port", "Port", FieldType.Number, DefaultValue: "587" ),
            new( "UseSsl", "Use SSL/TLS", FieldType.Bool, DefaultValue: "true" ),
            new( "CredentialName", "Credential Name", FieldType.Text, Placeholder: "smtp-credentials", HelpText: "Secret store key holding {\"username\":\"…\",\"password\":\"…\"}. Leave blank for anonymous." ),
            new( "From", "From", FieldType.Text, Required: true, Placeholder: "noreply@example.com" ),
            new( "To", "To (comma-separated)", FieldType.Text, Required: true, Placeholder: "alice@example.com,bob@example.com" ),
            new( "Cc", "CC (comma-separated)", FieldType.Text, Placeholder: "manager@example.com" ),
            new( "Subject", "Subject", FieldType.Text, Required: true, Placeholder: "Workflow notification" ),
            new( "Body", "Body", FieldType.TextArea, Placeholder: "Email body…", HelpText: "Optional body. When omitted the input variable value is used." ),
            new( "IsHtml", "HTML Body", FieldType.Bool, DefaultValue: "false" ),
            new( "Attachments", "Attachments (comma-separated paths)", FieldType.Text, Placeholder: "C:\\reports\\report.pdf" ),
        ] ),
    ];

    /// <summary>Fast lookup by action key (case-insensitive).</summary>
    public static readonly FrozenDictionary<string, ActionFormDescriptor> Actions =
        s_allDescriptors.ToFrozenDictionary( d => d.Key, StringComparer.OrdinalIgnoreCase );

    /// <summary>Ordered list of all descriptors for populating dropdowns.</summary>
    public static IReadOnlyList<ActionFormDescriptor> All => s_allDescriptors;
}
