using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>ExpandArchive</c> action - extracts a Zip or TarGz archive
/// to a destination directory. Includes zip-slip protection.
/// </summary>
public sealed class ExpandArchiveHandler : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<ExpandArchiveHandler> _logger;

    /// <summary>Creates a new <see cref="ExpandArchiveHandler"/>.</summary>
    public ExpandArchiveHandler( IFilePathResolver resolver, ILogger<ExpandArchiveHandler> logger ) {
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Action => "ExpandArchive";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            ExpandArchiveParameters p = parameters.Deserialize<ExpandArchiveParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize ExpandArchive parameters." );

            string sourcePath = _resolver.ResolveSinglePath( p.Source );
            string destPath = _resolver.ResolveSinglePath( p.Destination );

            if (!File.Exists( sourcePath )) {
                throw new FileNotFoundException( $"Archive not found: '{sourcePath}'" );
            }

            _ = Directory.CreateDirectory( destPath );

            ArchiveFormat format = p.Format == ArchiveFormat.Auto
                ? DetectFormat( sourcePath )
                : p.Format;

            int count;
            if (format == ArchiveFormat.Zip) {
                count = await ExtractZipAsync( sourcePath, destPath, p.Overwrite, output, cancellationToken );
            } else {
                count = await ExtractTarGzAsync( sourcePath, destPath, p.Overwrite, output, cancellationToken );
            }

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"ExpandArchive: extracted {count} entry/entries from '{sourcePath}' to '{destPath}'" ),
                cancellationToken );

            return new ActionOperatorResult( Success: true, OutputVariableValue: JsonSerializer.Serialize( destPath, ActionJson.SerializerOptions ) );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError( ex, "ExpandArchive action failed" );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"ExpandArchive failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    /// <summary>Detects archive format from file extension.</summary>
    private static ArchiveFormat DetectFormat( string path ) {
        string lowerPath = path.ToLowerInvariant( );
        if (lowerPath.EndsWith( ".zip", StringComparison.Ordinal )) {
            return ArchiveFormat.Zip;
        }
        if (lowerPath.EndsWith( ".tar.gz", StringComparison.Ordinal ) ||
            lowerPath.EndsWith( ".tgz", StringComparison.Ordinal )) {
            return ArchiveFormat.TarGz;
        }
        throw new ArgumentException(
            $"Cannot auto-detect archive format from extension: '{Path.GetFileName( path )}'. " +
            "Specify the Format parameter explicitly." );
    }

    /// <summary>Validates that an entry path does not escape the destination directory (zip-slip protection).</summary>
    private static void ValidateEntryPath( string entryFullPath, string destinationDir ) {
        string fullDest = Path.GetFullPath( destinationDir + Path.DirectorySeparatorChar );
        string fullEntry = Path.GetFullPath( entryFullPath );

        if (!fullEntry.StartsWith( fullDest, StringComparison.OrdinalIgnoreCase )) {
            throw new IOException(
                $"Archive entry would extract outside the destination directory: '{fullEntry}'. " +
                "This may indicate a zip-slip attack." );
        }
    }

    /// <summary>Extracts a Zip archive to the destination.</summary>
    private static async Task<int> ExtractZipAsync(
        string sourcePath,
        string destPath,
        bool overwrite,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken
    ) {
        using ZipArchive archive = ZipFile.OpenRead( sourcePath );
        int count = 0;

        foreach (ZipArchiveEntry entry in archive.Entries) {
            cancellationToken.ThrowIfCancellationRequested( );

            // Skip directory entries
            if (string.IsNullOrEmpty( entry.Name )) {
                continue;
            }

            string entryFullPath = Path.Combine( destPath, entry.FullName );
            ValidateEntryPath( entryFullPath, destPath );

            string? entryDir = Path.GetDirectoryName( entryFullPath );
            if (entryDir is not null) {
                _ = Directory.CreateDirectory( entryDir );
            }

            if (File.Exists( entryFullPath ) && !overwrite) {
                throw new IOException( $"File already exists: '{entryFullPath}'. Set Overwrite to true to replace." );
            }

            entry.ExtractToFile( entryFullPath, overwrite );
            count++;

            if (count % 100 == 0) {
                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information, $"ExpandArchive: {count} entries extracted..." ),
                    cancellationToken );
            }
        }

        return count;
    }

    /// <summary>Extracts a TarGz archive to the destination.</summary>
    private static async Task<int> ExtractTarGzAsync(
        string sourcePath,
        string destPath,
        bool overwrite,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken
    ) {
        await using FileStream fs = new( sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read );
        await using GZipStream gzStream = new( fs, CompressionMode.Decompress );
        await using TarReader tarReader = new( gzStream );

        int count = 0;
        TarEntry? entry;

        while ((entry = await tarReader.GetNextEntryAsync( false, cancellationToken )) is not null) {
            cancellationToken.ThrowIfCancellationRequested( );

            if (entry.EntryType is TarEntryType.Directory) {
                string dirPath = Path.Combine( destPath, entry.Name );
                ValidateEntryPath( dirPath, destPath );
                _ = Directory.CreateDirectory( dirPath );
                continue;
            }

            if (entry.EntryType is not TarEntryType.RegularFile and
                not TarEntryType.V7RegularFile) {
                continue;
            }

            string entryFullPath = Path.Combine( destPath, entry.Name );
            ValidateEntryPath( entryFullPath, destPath );

            string? entryDir = Path.GetDirectoryName( entryFullPath );
            if (entryDir is not null) {
                _ = Directory.CreateDirectory( entryDir );
            }

            if (File.Exists( entryFullPath ) && !overwrite) {
                throw new IOException( $"File already exists: '{entryFullPath}'. Set Overwrite to true to replace." );
            }

            await entry.ExtractToFileAsync( entryFullPath, overwrite, cancellationToken );
            count++;

            if (count % 100 == 0) {
                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information, $"ExpandArchive: {count} entries extracted..." ),
                    cancellationToken );
            }
        }

        return count;
    }
}
