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
/// Unit tests for the <see cref="MoveFileHandler"/> action handler.
/// Validates moving a single file, moving an entire directory, handling of
/// no-match wildcards, and the same-source-and-destination guard.
/// </summary>
[TestClass]
public class MoveFileHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private MoveFileHandler _handler = null!;
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
        _handler = new MoveFileHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<MoveFileHandler>.Instance
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
    /// Verifies that moving a single file to a new destination succeeds,
    /// removes the source, and creates the destination with the original content.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task MoveSingleFile_Succeeds( ) {
        string src = Path.Combine(
            _tempDir,
            "source.txt"
        );
        string dest = Path.Combine(
            _tempDir,
            "dest.txt"
        );
        await File.WriteAllTextAsync(
            src,
            "hello",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize( new MoveFileParameters { Source = src, Destination = dest } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( File.Exists( src ) );
        Assert.IsTrue( File.Exists( dest ) );
        Assert.AreEqual(
            "hello",
            await File.ReadAllTextAsync(
                dest,
                TestContext.CancellationToken
            )
        );
    }

    /// <summary>
    /// Verifies that moving an entire directory to a new location succeeds,
    /// removing the source directory and placing all contents at the destination.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task MoveDirectory_Succeeds( ) {
        string srcDir = Path.Combine(
            _tempDir,
            "srcDir"
        );
        _ = Directory.CreateDirectory( srcDir );
        await File.WriteAllTextAsync(
            Path.Combine( srcDir, "file.txt" ),
            "data",
            TestContext.CancellationToken
        );

        string destDir = Path.Combine(
            _tempDir,
            "destDir"
        );

        JsonElement parameters = Serialize( new MoveFileParameters { Source = srcDir, Destination = destDir } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( Directory.Exists( srcDir ) );
        Assert.IsTrue( File.Exists( Path.Combine( destDir, "file.txt" ) ) );
    }

    /// <summary>
    /// Verifies that a wildcard source that matches zero files returns a failure result.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task MoveNoMatch_ReturnsFailure( ) {
        JsonElement parameters = Serialize( new MoveFileParameters {
            Source = Path.Combine( _tempDir, "*.xyz" ),
            Destination = Path.Combine( _tempDir, "out" )
        } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }

    /// <summary>
    /// Verifies that specifying the same path as both source and destination
    /// returns a failure result with a non-null exception.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task MoveSameSourceAndDest_ThrowsArgument( ) {
        string path = Path.Combine(
            _tempDir,
            "file.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "data",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize( new MoveFileParameters { Source = path, Destination = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }
}
