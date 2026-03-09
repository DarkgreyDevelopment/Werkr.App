using System.IO.Compression;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Agent.Operators.Actions;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="CompressArchiveHandler"/> action handler.
/// Validates Zip and TarGz compression, overwrite behavior, and edge cases.
/// </summary>
[TestClass]
public class CompressArchiveHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private CompressArchiveHandler _handler = null!;
    /// <summary>
    /// Unbounded channel used to capture <see cref="OperatorOutput"/> messages.
    /// </summary>
    private Channel<OperatorOutput> _channel = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates a unique temporary directory, the handler, and an unbounded output channel.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine(
            Path.GetTempPath( ),
            $"werkr-test-{Guid.NewGuid( )}"
        );
        _ = Directory.CreateDirectory( _tempDir );
        _handler = new CompressArchiveHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<CompressArchiveHandler>.Instance
        );
        _channel = Channel.CreateUnbounded<OperatorOutput>( );
    }

    /// <summary>
    /// Deletes the temporary directory and all its contents.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        if (Directory.Exists( _tempDir )) {
            Directory.Delete(
                _tempDir,
                recursive: true
            );
        }
    }

    /// <summary>
    /// Serializes a value to a <see cref="JsonElement"/> using the shared test serializer.
    /// </summary>
    private static JsonElement Serialize<T>( T value ) =>
        TestActionDescriptor.Serialize( value );

    /// <summary>
    /// Verifies that compressing a single file into a Zip archive succeeds.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CompressArchive_SingleFile_Zip_Succeeds( ) {
        string sourceFile = Path.Combine( _tempDir, "data.txt" );
        await File.WriteAllTextAsync( sourceFile, "hello world", TestContext.CancellationToken );
        string destFile = Path.Combine( _tempDir, "archive.zip" );

        JsonElement parameters = Serialize( new CompressArchiveParameters {
            Source = sourceFile,
            Destination = destFile,
            Format = ArchiveFormat.Zip
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( destFile ) );

        // Verify archive contains the file
        using ZipArchive archive = ZipFile.OpenRead( destFile );
        Assert.HasCount( 1, archive.Entries );
        Assert.AreEqual( "data.txt", archive.Entries[0].Name );
    }

    /// <summary>
    /// Verifies that compressing a directory into a Zip archive includes all files.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CompressArchive_Directory_Zip_IncludesAllFiles( ) {
        string sourceDir = Path.Combine( _tempDir, "src" );
        _ = Directory.CreateDirectory( sourceDir );
        await File.WriteAllTextAsync( Path.Combine( sourceDir, "a.txt" ), "a", TestContext.CancellationToken );
        await File.WriteAllTextAsync( Path.Combine( sourceDir, "b.txt" ), "b", TestContext.CancellationToken );
        string subDir = Path.Combine( sourceDir, "sub" );
        _ = Directory.CreateDirectory( subDir );
        await File.WriteAllTextAsync( Path.Combine( subDir, "c.txt" ), "c", TestContext.CancellationToken );

        string destFile = Path.Combine( _tempDir, "archive.zip" );

        JsonElement parameters = Serialize( new CompressArchiveParameters {
            Source = sourceDir,
            Destination = destFile,
            Format = ArchiveFormat.Zip
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );

        using ZipArchive archive = ZipFile.OpenRead( destFile );
        Assert.HasCount( 3, archive.Entries );
    }

    /// <summary>
    /// Verifies that compressing to TarGz format succeeds and produces a valid file.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CompressArchive_SingleFile_TarGz_Succeeds( ) {
        string sourceFile = Path.Combine( _tempDir, "data.txt" );
        await File.WriteAllTextAsync( sourceFile, "hello world", TestContext.CancellationToken );
        string destFile = Path.Combine( _tempDir, "archive.tar.gz" );

        JsonElement parameters = Serialize( new CompressArchiveParameters {
            Source = sourceFile,
            Destination = destFile,
            Format = ArchiveFormat.TarGz
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( destFile ) );
        Assert.IsGreaterThan( 0L, new FileInfo( destFile ).Length );
    }

    /// <summary>
    /// Verifies that existing archives are not overwritten when Overwrite is false.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CompressArchive_ExistingArchive_NoOverwrite_Fails( ) {
        string sourceFile = Path.Combine( _tempDir, "data.txt" );
        await File.WriteAllTextAsync( sourceFile, "hello", TestContext.CancellationToken );
        string destFile = Path.Combine( _tempDir, "archive.zip" );
        await File.WriteAllTextAsync( destFile, "existing", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new CompressArchiveParameters {
            Source = sourceFile,
            Destination = destFile,
            Format = ArchiveFormat.Zip,
            Overwrite = false
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsInstanceOfType<IOException>( result.Exception );
    }

    /// <summary>
    /// Verifies that Overwrite=true replaces an existing archive.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CompressArchive_ExistingArchive_Overwrite_Succeeds( ) {
        string sourceFile = Path.Combine( _tempDir, "data.txt" );
        await File.WriteAllTextAsync( sourceFile, "hello", TestContext.CancellationToken );
        string destFile = Path.Combine( _tempDir, "archive.zip" );
        await File.WriteAllTextAsync( destFile, "existing", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new CompressArchiveParameters {
            Source = sourceFile,
            Destination = destFile,
            Format = ArchiveFormat.Zip,
            Overwrite = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that Format.Auto is rejected for compression.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CompressArchive_AutoFormat_ReturnsFailure( ) {
        string sourceFile = Path.Combine( _tempDir, "data.txt" );
        await File.WriteAllTextAsync( sourceFile, "hello", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new CompressArchiveParameters {
            Source = sourceFile,
            Destination = Path.Combine( _tempDir, "archive.zip" ),
            Format = ArchiveFormat.Auto
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }

    /// <summary>
    /// Verifies that a denied source path returns failure with UnauthorizedAccessException.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CompressArchive_DeniedPath_ReturnsFailure( ) {
        CompressArchiveHandler denied = new(
            TestFilePathResolver.DenyAll,
            NullLogger<CompressArchiveHandler>.Instance
        );

        JsonElement parameters = Serialize( new CompressArchiveParameters {
            Source = "/some/path",
            Destination = "/some/dest.zip"
        } );
        ActionOperatorResult result = await denied.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }
}
