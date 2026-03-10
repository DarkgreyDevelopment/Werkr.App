using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Operators.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="TestExistsHandler"/> action handler.
/// Validates existence checks for files, directories, and the "any" type,
/// as well as non-existence scenarios and type mismatches (e.g., asking for
/// a file when a directory exists at the path).
/// </summary>
[TestClass]
public class TestExistsHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private TestExistsHandler _handler = null!;
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
        _handler = new TestExistsHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<TestExistsHandler>.Instance
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
    /// Verifies that checking an existing file with <see cref="PathType.File"/> returns success.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FileExists_ReturnsSuccess( ) {
        string path = Path.Combine(
            _tempDir,
            "file.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "data",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.File } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that checking a missing file with <see cref="PathType.File"/> returns failure.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FileNotExists_ReturnsFailure( ) {
        string path = Path.Combine(
            _tempDir,
            "missing.txt"
        );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.File } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }

    /// <summary>
    /// Verifies that checking an existing directory with <see cref="PathType.Directory"/> returns success.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DirectoryExists_ReturnsSuccess( ) {
        string path = Path.Combine(
            _tempDir,
            "subdir"
        );
        _ = Directory.CreateDirectory( path );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.Directory } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that checking a missing directory with <see cref="PathType.Directory"/> returns failure.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DirectoryNotExists_ReturnsFailure( ) {
        string path = Path.Combine(
            _tempDir,
            "missing-dir"
        );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.Directory } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }

    /// <summary>
    /// Verifies that checking an existing file with <see cref="PathType.Any"/> returns success.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task AnyType_FileExists_ReturnsSuccess( ) {
        string path = Path.Combine(
            _tempDir,
            "file.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "data",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.Any } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that checking an existing directory with <see cref="PathType.Any"/> returns success.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task AnyType_DirectoryExists_ReturnsSuccess( ) {
        string path = Path.Combine(
            _tempDir,
            "dir"
        );
        _ = Directory.CreateDirectory( path );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.Any } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that checking a directory path with <see cref="PathType.File"/>
    /// returns failure, because the path exists as a directory rather than
    /// a file.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task FileType_OnDirectory_ReturnsFailure( ) {
        string path = Path.Combine(
            _tempDir,
            "dir"
        );
        _ = Directory.CreateDirectory( path );

        JsonElement parameters = Serialize( new TestExistsParameters { Path = path, Type = PathType.File } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }
}
