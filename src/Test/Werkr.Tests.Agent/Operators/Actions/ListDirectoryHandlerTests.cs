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
/// Unit tests for the <see cref="ListDirectoryHandler"/> action handler.
/// Validates directory enumeration with pattern matching, type filtering, sorting, and recursion.
/// </summary>
[TestClass]
public class ListDirectoryHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private ListDirectoryHandler _handler = null!;
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
        _handler = new ListDirectoryHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<ListDirectoryHandler>.Instance
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
    /// Collects all messages from the output channel after completing the writer.
    /// </summary>
    private async Task<List<OperatorOutput>> CollectOutputAsync( ) {
        _channel.Writer.Complete( );
        List<OperatorOutput> messages = [ ];
        await foreach (OperatorOutput msg in _channel.Reader.ReadAllAsync( TestContext.CancellationToken )) {
            messages.Add( msg );
        }
        return messages;
    }

    /// <summary>
    /// Verifies that listing files only returns files, not directories.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ListDirectory_FilesOnly_ReturnsFiles( ) {
        await File.WriteAllTextAsync( Path.Combine( _tempDir, "a.txt" ), "content", TestContext.CancellationToken );
        await File.WriteAllTextAsync( Path.Combine( _tempDir, "b.txt" ), "content", TestContext.CancellationToken );
        _ = Directory.CreateDirectory( Path.Combine( _tempDir, "sub" ) );

        JsonElement parameters = Serialize( new ListDirectoryParameters {
            Path = _tempDir,
            Type = PathType.File
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );

        List<OperatorOutput> messages = await CollectOutputAsync( );
        // Second message is the JSON array
        string[] files = JsonSerializer.Deserialize<string[]>( messages[1].Message )!;
        Assert.HasCount( 2, files );
    }

    /// <summary>
    /// Verifies that listing directories only returns directories, not files.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ListDirectory_DirectoriesOnly_ReturnsDirectories( ) {
        await File.WriteAllTextAsync( Path.Combine( _tempDir, "a.txt" ), "content", TestContext.CancellationToken );
        _ = Directory.CreateDirectory( Path.Combine( _tempDir, "sub1" ) );
        _ = Directory.CreateDirectory( Path.Combine( _tempDir, "sub2" ) );

        JsonElement parameters = Serialize( new ListDirectoryParameters {
            Path = _tempDir,
            Type = PathType.Directory
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );

        List<OperatorOutput> messages = await CollectOutputAsync( );
        string[] dirs = JsonSerializer.Deserialize<string[]>( messages[1].Message )!;
        Assert.HasCount( 2, dirs );
    }

    /// <summary>
    /// Verifies that a glob pattern filters entries correctly.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ListDirectory_PatternFilter_ReturnsMatching( ) {
        await File.WriteAllTextAsync( Path.Combine( _tempDir, "data.csv" ), "a", TestContext.CancellationToken );
        await File.WriteAllTextAsync( Path.Combine( _tempDir, "notes.txt" ), "b", TestContext.CancellationToken );
        await File.WriteAllTextAsync( Path.Combine( _tempDir, "report.csv" ), "c", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new ListDirectoryParameters {
            Path = _tempDir,
            Pattern = "*.csv"
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );

        List<OperatorOutput> messages = await CollectOutputAsync( );
        string[] files = JsonSerializer.Deserialize<string[]>( messages[1].Message )!;
        Assert.HasCount( 2, files );
    }

    /// <summary>
    /// Verifies that recursive enumeration includes entries from subdirectories.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ListDirectory_Recursive_IncludesSubdirectories( ) {
        string subDir = Path.Combine( _tempDir, "sub" );
        _ = Directory.CreateDirectory( subDir );
        await File.WriteAllTextAsync( Path.Combine( _tempDir, "top.txt" ), "a", TestContext.CancellationToken );
        await File.WriteAllTextAsync( Path.Combine( subDir, "nested.txt" ), "b", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new ListDirectoryParameters {
            Path = _tempDir,
            Recursive = true
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );

        List<OperatorOutput> messages = await CollectOutputAsync( );
        string[] files = JsonSerializer.Deserialize<string[]>( messages[1].Message )!;
        Assert.HasCount( 2, files );
    }

    /// <summary>
    /// Verifies that listing an empty directory succeeds with an empty JSON array.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ListDirectory_EmptyDirectory_ReturnsEmptyArray( ) {
        JsonElement parameters = Serialize( new ListDirectoryParameters { Path = _tempDir } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );

        List<OperatorOutput> messages = await CollectOutputAsync( );
        string[] files = JsonSerializer.Deserialize<string[]>( messages[1].Message )!;
        Assert.IsEmpty( files );
    }

    /// <summary>
    /// Verifies that listing a non-existent directory returns failure.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ListDirectory_NonExistentDirectory_ReturnsFailure( ) {
        string missing = Path.Combine( _tempDir, "does-not-exist" );

        JsonElement parameters = Serialize( new ListDirectoryParameters { Path = missing } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<DirectoryNotFoundException>( result.Exception );
    }

    /// <summary>
    /// Verifies that a denied path returns failure with UnauthorizedAccessException.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ListDirectory_DeniedPath_ReturnsFailure( ) {
        ListDirectoryHandler denied = new(
            TestFilePathResolver.DenyAll,
            NullLogger<ListDirectoryHandler>.Instance
        );

        JsonElement parameters = Serialize( new ListDirectoryParameters { Path = "/some/path" } );
        ActionOperatorResult result = await denied.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }
}
