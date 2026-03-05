using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Agent.Operators.Actions;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="CreateDirectoryHandler"/> action handler.
/// Validates successful creation, idempotent creation when the directory
/// already exists, nested directory creation, and failure when a file
/// occupies the target path.
/// </summary>
[TestClass]
public class CreateDirectoryHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private CreateDirectoryHandler _handler = null!;
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
        _handler = new CreateDirectoryHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<CreateDirectoryHandler>.Instance
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
    /// Verifies that creating a new directory at a valid path succeeds and the directory exists afterward.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateDirectory_Succeeds( ) {
        string path = Path.Combine(
            _tempDir,
            "newDir"
        );

        JsonElement parameters = Serialize( new CreateDirectoryParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( Directory.Exists( path ) );
    }

    /// <summary>
    /// Verifies that creating a directory that already exists is idempotent and still succeeds.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateDirectory_AlreadyExists_StillSucceeds( ) {
        string path = Path.Combine(
            _tempDir,
            "existingDir"
        );
        _ = Directory.CreateDirectory( path );

        JsonElement parameters = Serialize( new CreateDirectoryParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that a nested multi-level directory path is created in its entirety.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateDirectory_NestedPath_CreatesAll( ) {
        string path = Path.Combine(
            _tempDir,
            "a",
            "b",
            "c"
        );

        JsonElement parameters = Serialize( new CreateDirectoryParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( Directory.Exists( path ) );
    }

    /// <summary>
    /// Verifies that if a regular file already exists at the target path,
    /// the handler returns a failure result with a non-null exception.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateDirectory_FileExistsAtPath_ReturnsFailure( ) {
        string path = Path.Combine(
            _tempDir,
            "conflicting"
        );
        await File.WriteAllTextAsync(
            path,
            "data",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize( new CreateDirectoryParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }
}
