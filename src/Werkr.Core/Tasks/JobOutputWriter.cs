using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Werkr.Common.Configuration;
using Werkr.Core.Communication;

namespace Werkr.Core.Tasks;

/// <summary>
/// Writes job output to individual log files on disk.
/// Each job gets a file named <c>{JobId}.log</c> in the configured output directory.
/// Output is appended incrementally, so partial output is preserved if the job crashes.
/// </summary>
/// <param name="options">Job output configuration.</param>
/// <param name="logger">Logger instance.</param>
public sealed class JobOutputWriter(
    IOptions<JobOutputOptions> options,
    ILogger<JobOutputWriter> logger
) {

    private readonly string _outputDirectory = options.Value.OutputDirectory;
    private readonly int _tailPreviewLength = options.Value.TailPreviewLength;

    /// <summary>
    /// Ensures the output directory exists. Called once at service startup or first use.
    /// </summary>
    public void EnsureDirectoryExists( ) {
        if (!Directory.Exists( _outputDirectory )) {
            _ = Directory.CreateDirectory( _outputDirectory );
            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "Created job output directory: {Directory}",
                    _outputDirectory
                );
            }
        }
    }

    /// <summary>
    /// Returns the full file path for a job's output log.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <returns>Absolute or relative path to the log file.</returns>
    public string GetOutputPath( Guid jobId ) =>
        Path.Combine(
            _outputDirectory,
            $"{jobId}.log"
        );

    /// <summary>
    /// Writes an operator output line to the job's log file, appending to any existing content.
    /// Format: <c>[timestamp] [level] message</c>
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="output">The operator output record to write.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task WriteLineAsync(
        Guid jobId,
        OperatorOutput output,
        CancellationToken ct = default
    ) {
        EnsureDirectoryExists( );
        string filePath = GetOutputPath( jobId );
        string line = FormatLine( output );
        await File.AppendAllTextAsync(
            filePath,
            line + Environment.NewLine,
            ct
        );
    }

    /// <summary>
    /// Writes multiple operator output lines to the job's log file.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="outputs">The operator output records to write.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task WriteLinesAsync(
        Guid jobId,
        IEnumerable<OperatorOutput> outputs,
        CancellationToken ct = default
    ) {
        EnsureDirectoryExists( );
        string filePath = GetOutputPath( jobId );
        IEnumerable<string> lines = outputs.Select( o => FormatLine( o ) );
        await File.AppendAllLinesAsync(
            filePath,
            lines,
            ct
        );
    }

    /// <summary>
    /// Reads the full output file for a job. Returns null if the file does not exist.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Full file contents, or null if no output was written.</returns>
    public async Task<string?> ReadFullOutputAsync(
        Guid jobId,
        CancellationToken ct = default
    ) {
        string filePath = GetOutputPath( jobId );
        return !File.Exists( filePath ) ? null : await File.ReadAllTextAsync(
            filePath,
            ct
        );
    }

    /// <summary>
    /// Extracts the tail preview (last N characters) from a job's output file.
    /// Uses the configured <see cref="JobOutputOptions.TailPreviewLength"/>.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tail preview string, or null if no output exists.</returns>
    public async Task<string?> GetTailPreviewAsync(
        Guid jobId,
        CancellationToken ct = default
    ) {
        string filePath = GetOutputPath( jobId );
        if (!File.Exists( filePath )) {
            return null;
        }

        string content = await File.ReadAllTextAsync(
            filePath,
            ct
        );
        return content.Length <= _tailPreviewLength
            ? content
            : content[^_tailPreviewLength..];
    }

    /// <summary>
    /// Formats an <see cref="OperatorOutput"/> record into a log line.
    /// </summary>
    private static string FormatLine( OperatorOutput output ) =>
        $"[{output.Timestamp}] [{output.LogLevel}] {output.Message}";
}
