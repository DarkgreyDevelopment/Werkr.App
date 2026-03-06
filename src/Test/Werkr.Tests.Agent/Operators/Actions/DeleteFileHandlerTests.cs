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
/// Unit tests for the <see cref="DeleteFileHandler"/> action handler.
/// Validates deletion of a single file, recursive directory deletion,
/// failure when the target does not exist, and forced removal of
/// read-only files.
/// </summary>
[TestClass]
public class DeleteFileHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private DeleteFileHandler _handler = null!;
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
        _handler = new DeleteFileHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<DeleteFileHandler>.Instance
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
    /// Verifies that deleting an existing file succeeds and the file no longer exists.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DeleteFile_Succeeds( ) {
        string path = Path.Combine(
            _tempDir,
            "file.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "data",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize( new DeleteFileParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( File.Exists( path ) );
    }

    /// <summary>
    /// Verifies that deleting a directory with the <c>Recursive</c> flag
    /// removes the directory and all of its contents.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DeleteDirectory_Recursive( ) {
        string dir = Path.Combine(
            _tempDir,
            "subdir"
        );
        _ = Directory.CreateDirectory( dir );
        await File.WriteAllTextAsync(
            Path.Combine( dir, "file.txt" ),
            "data",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize( new DeleteFileParameters { Path = dir, Recursive = true } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( Directory.Exists( dir ) );
    }

    /// <summary>
    /// Verifies that attempting to delete a path that does not exist returns a failure result.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DeleteNonExistent_ReturnsFailure( ) {
        string path = Path.Combine(
            _tempDir,
            "missing.txt"
        );

        JsonElement parameters = Serialize( new DeleteFileParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }

    /// <summary>
    /// Verifies that deleting a read-only file with the <c>Force</c> flag succeeds and the file is removed.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DeleteReadOnly_ForceRemoves( ) {
        string path = Path.Combine(
            _tempDir,
            "readonly.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "data",
            TestContext.CancellationToken
        );
        File.SetAttributes( path, FileAttributes.ReadOnly );

        JsonElement parameters = Serialize( new DeleteFileParameters { Path = path, Force = true } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( File.Exists( path ) );
    }
}
