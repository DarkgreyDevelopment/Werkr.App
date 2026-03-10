using System.IO.Compression;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Operators.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="ExpandArchiveHandler"/> action handler.
/// Validates Zip and TarGz extraction, auto-detection, overwrite, and zip-slip protection.
/// </summary>
[TestClass]
public class ExpandArchiveHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private ExpandArchiveHandler _handler = null!;
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
        _handler = new ExpandArchiveHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<ExpandArchiveHandler>.Instance
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

    /// <summary>Creates a test Zip archive at the specified path with the given entries.</summary>
    private static void CreateTestZip( string archivePath, params (string EntryName, string Content)[] entries ) {
        using FileStream fs = new( archivePath, FileMode.Create, FileAccess.Write, FileShare.None );
        using ZipArchive archive = new( fs, ZipArchiveMode.Create );
        foreach ((string entryName, string content) in entries) {
            ZipArchiveEntry entry = archive.CreateEntry( entryName );
            using StreamWriter writer = new( entry.Open( ) );
            writer.Write( content );
        }
    }

    /// <summary>
    /// Verifies that extracting a Zip archive succeeds and files are on disk.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ExpandArchive_Zip_ExtractsFiles( ) {
        string archivePath = Path.Combine( _tempDir, "test.zip" );
        CreateTestZip( archivePath, ("hello.txt", "world"), ("sub/nested.txt", "nested") );
        string destDir = Path.Combine( _tempDir, "output" );

        JsonElement parameters = Serialize( new ExpandArchiveParameters {
            Source = archivePath,
            Destination = destDir,
            Format = ArchiveFormat.Zip
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( Path.Combine( destDir, "hello.txt" ) ) );
        Assert.IsTrue( File.Exists( Path.Combine( destDir, "sub", "nested.txt" ) ) );
        Assert.AreEqual( "world", await File.ReadAllTextAsync(
            Path.Combine( destDir, "hello.txt" ), TestContext.CancellationToken ) );
    }

    /// <summary>
    /// Verifies that auto-detection correctly identifies a .zip file.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ExpandArchive_AutoDetect_Zip_Succeeds( ) {
        string archivePath = Path.Combine( _tempDir, "test.zip" );
        CreateTestZip( archivePath, ("file.txt", "content") );
        string destDir = Path.Combine( _tempDir, "output" );

        JsonElement parameters = Serialize( new ExpandArchiveParameters {
            Source = archivePath,
            Destination = destDir
            // Format defaults to Auto
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( Path.Combine( destDir, "file.txt" ) ) );
    }

    /// <summary>
    /// Verifies that extraction fails when a file already exists and Overwrite is false.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ExpandArchive_ExistingFile_NoOverwrite_Fails( ) {
        string archivePath = Path.Combine( _tempDir, "test.zip" );
        CreateTestZip( archivePath, ("file.txt", "new content") );
        string destDir = Path.Combine( _tempDir, "output" );
        _ = Directory.CreateDirectory( destDir );
        await File.WriteAllTextAsync(
            Path.Combine( destDir, "file.txt" ), "old content", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new ExpandArchiveParameters {
            Source = archivePath,
            Destination = destDir,
            Overwrite = false
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<IOException>( result.Exception );
    }

    /// <summary>
    /// Verifies that Overwrite=true replaces existing files during extraction.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ExpandArchive_ExistingFile_Overwrite_Succeeds( ) {
        string archivePath = Path.Combine( _tempDir, "test.zip" );
        CreateTestZip( archivePath, ("file.txt", "new content") );
        string destDir = Path.Combine( _tempDir, "output" );
        _ = Directory.CreateDirectory( destDir );
        await File.WriteAllTextAsync(
            Path.Combine( destDir, "file.txt" ), "old content", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new ExpandArchiveParameters {
            Source = archivePath,
            Destination = destDir,
            Overwrite = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        string content = await File.ReadAllTextAsync(
            Path.Combine( destDir, "file.txt" ), TestContext.CancellationToken );
        Assert.AreEqual( "new content", content );
    }

    /// <summary>
    /// Verifies that a zip-slip attack vector (entry with ../) is rejected.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ExpandArchive_ZipSlip_Rejected( ) {
        // Create a malicious zip with a path traversal entry
        string archivePath = Path.Combine( _tempDir, "malicious.zip" );
        using (FileStream fs = new( archivePath, FileMode.Create, FileAccess.Write, FileShare.None )) {
            using ZipArchive archive = new( fs, ZipArchiveMode.Create );
            ZipArchiveEntry entry = archive.CreateEntry( "../../../evil.txt" );
            using StreamWriter writer = new( entry.Open( ) );
            writer.Write( "evil content" );
        }

        string destDir = Path.Combine( _tempDir, "output" );

        JsonElement parameters = Serialize( new ExpandArchiveParameters {
            Source = archivePath,
            Destination = destDir
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<IOException>( result.Exception );
        Assert.Contains( "zip-slip", result.Exception!.Message );
    }

    /// <summary>
    /// Verifies that an unrecognized extension with Auto format returns failure.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ExpandArchive_UnrecognizedExtension_Auto_Fails( ) {
        string archivePath = Path.Combine( _tempDir, "test.xyz" );
        await File.WriteAllTextAsync( archivePath, "not an archive", TestContext.CancellationToken );
        string destDir = Path.Combine( _tempDir, "output" );

        JsonElement parameters = Serialize( new ExpandArchiveParameters {
            Source = archivePath,
            Destination = destDir
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }

    /// <summary>
    /// Verifies that a missing archive file returns failure.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ExpandArchive_MissingArchive_ReturnsFailure( ) {
        string missingPath = Path.Combine( _tempDir, "missing.zip" );
        string destDir = Path.Combine( _tempDir, "output" );

        JsonElement parameters = Serialize( new ExpandArchiveParameters {
            Source = missingPath,
            Destination = destDir
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<FileNotFoundException>( result.Exception );
    }

    /// <summary>
    /// Verifies that a denied path returns failure with UnauthorizedAccessException.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ExpandArchive_DeniedPath_ReturnsFailure( ) {
        ExpandArchiveHandler denied = new(
            TestFilePathResolver.DenyAll,
            NullLogger<ExpandArchiveHandler>.Instance
        );

        JsonElement parameters = Serialize( new ExpandArchiveParameters {
            Source = "/some/archive.zip",
            Destination = "/some/dest"
        } );
        ActionOperatorResult result = await denied.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }
}
