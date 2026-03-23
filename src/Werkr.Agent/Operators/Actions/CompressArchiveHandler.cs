using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Attributes;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>CompressArchive</c> action - creates a Zip or TarGz archive
/// from a source path, directory, or glob pattern.
/// </summary>
/// <remarks>Creates a new <see cref="CompressArchiveHandler"/>.</remarks>
[ActionCategory( "Archive" )]
public sealed partial class CompressArchiveHandler( IFilePathResolver resolver, ILogger<CompressArchiveHandler> logger ) : IActionHandler {

    /// <summary>
    /// Resolves and validates file paths against the agent's allowed-path allowlist.
    /// </summary>
    private readonly IFilePathResolver _resolver = resolver;
    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<CompressArchiveHandler> _logger = logger;

    /// <inheritdoc/>
    public string Action => "CompressArchive";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            CompressArchiveParameters p = parameters.Deserialize<CompressArchiveParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize CompressArchive parameters." );

            if (p.Format == ArchiveFormat.Auto) {
                throw new ArgumentException( "ArchiveFormat.Auto is only valid for extraction, not compression." );
            }

            string destPath = _resolver.ResolveSinglePath( p.Destination );

            if (File.Exists( destPath ) && !p.Overwrite) {
                throw new IOException( $"Destination archive already exists: '{destPath}'. Set Overwrite to true to replace it." );
            }

            // Determine source files
            string sourcePath = _resolver.ResolveSinglePath( p.Source );
            List<(string FullPath, string EntryName)> filesToCompress = [ ];

            if (Directory.Exists( sourcePath )) {
                string baseDir = p.IncludeBaseDirectory
                    ? Path.GetDirectoryName( sourcePath )!
                    : sourcePath;

                foreach (string file in Directory.EnumerateFiles( sourcePath, "*", SearchOption.AllDirectories )) {
                    string entryName = Path.GetRelativePath( baseDir, file );
                    filesToCompress.Add( (file, entryName) );
                }
            } else if (File.Exists( sourcePath )) {
                filesToCompress.Add( (sourcePath, Path.GetFileName( sourcePath )) );
            } else {
                // Try as glob pattern
                string[] resolved = _resolver.ResolveFiles( p.Source );
                if (resolved.Length == 0) {
                    throw new FileNotFoundException( $"No files found matching source: '{p.Source}'" );
                }
                string commonDir = Path.GetDirectoryName( resolved[0] ) ?? ".";
                foreach (string file in resolved) {
                    string entryName = Path.GetRelativePath( commonDir, file );
                    filesToCompress.Add( (file, entryName) );
                }
            }

            if (filesToCompress.Count == 0) {
                throw new FileNotFoundException( $"No files found to compress from source: '{p.Source}'" );
            }

            // Delete existing archive if overwriting
            if (File.Exists( destPath )) {
                File.Delete( destPath );
            }

            if (p.Format == ArchiveFormat.Zip) {
                await CompressZipAsync( destPath, filesToCompress, p.CompressionLevel, output, cancellationToken );
            } else {
                await CompressTarGzAsync( destPath, filesToCompress, p.CompressionLevel, output, cancellationToken );
            }

            await output.WriteAsync(
                OperatorOutput.Create(
                    LogLevel.Information,
                    $"CompressArchive: created {p.Format} archive '{destPath}' with {filesToCompress.Count} file(s)" ),
                cancellationToken );

            return new ActionOperatorResult( Success: true, OutputVariableValue: JsonSerializer.Serialize( destPath, ActionJson.SerializerOptions ) );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"CompressArchive failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    /// <summary>Maps <see cref="ArchiveCompressionLevel"/> to <see cref="CompressionLevel"/>.</summary>
    private static CompressionLevel MapCompressionLevel( ArchiveCompressionLevel level ) => level switch {
        ArchiveCompressionLevel.Fastest => CompressionLevel.Fastest,
        ArchiveCompressionLevel.Optimal => CompressionLevel.Optimal,
        ArchiveCompressionLevel.SmallestSize => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal,
    };

    /// <summary>Creates a Zip archive from the specified files.</summary>
    private static async Task CompressZipAsync(
        string destPath,
        List<(string FullPath, string EntryName)> files,
        ArchiveCompressionLevel level,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken
    ) {
        CompressionLevel compressionLevel = MapCompressionLevel( level );

        using FileStream fs = new( destPath, FileMode.Create, FileAccess.Write, FileShare.None );
        using ZipArchive archive = new( fs, ZipArchiveMode.Create );

        int count = 0;
        foreach ((string fullPath, string entryName) in files) {
            cancellationToken.ThrowIfCancellationRequested( );

            ZipArchiveEntry entry = archive.CreateEntry(
                entryName.Replace( '\\', '/' ),
                compressionLevel );
            using Stream entryStream = entry.Open( );
            await using FileStream sourceStream = new( fullPath, FileMode.Open, FileAccess.Read, FileShare.Read );
            await sourceStream.CopyToAsync( entryStream, cancellationToken );
            count++;

            if (count % 100 == 0) {
                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information, $"CompressArchive: {count}/{files.Count} files added..." ),
                    cancellationToken );
            }
        }
    }

    /// <summary>Creates a TarGz archive from the specified files.</summary>
    private static async Task CompressTarGzAsync(
        string destPath,
        List<(string FullPath, string EntryName)> files,
        ArchiveCompressionLevel level,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken
    ) {
        CompressionLevel compressionLevel = MapCompressionLevel( level );

        await using FileStream fs = new( destPath, FileMode.Create, FileAccess.Write, FileShare.None );
        await using GZipStream gzStream = new( fs, compressionLevel );
        await using TarWriter tarWriter = new( gzStream );

        int count = 0;
        foreach ((string fullPath, string entryName) in files) {
            cancellationToken.ThrowIfCancellationRequested( );

            await tarWriter.WriteEntryAsync(
                fullPath,
                entryName.Replace( '\\', '/' ),
                cancellationToken );
            count++;

            if (count % 100 == 0) {
                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information, $"CompressArchive: {count}/{files.Count} files added..." ),
                    cancellationToken );
            }
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
