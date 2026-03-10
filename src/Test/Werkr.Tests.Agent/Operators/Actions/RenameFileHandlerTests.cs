using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Operators.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="RenameFileHandler"/> action handler.
/// Validates renaming a file, renaming a directory, failure when the source
/// does not exist, overwrite-guarded rename when the destination already exists,
/// and overwrite-enabled rename that replaces the existing destination.
/// </summary>
[TestClass]
public class RenameFileHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private RenameFileHandler _handler = null!;
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
        _handler = new RenameFileHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<RenameFileHandler>.Instance
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
    /// Verifies that renaming a file to a new name succeeds, removes the old
    /// file, and creates the file under the new name in the same directory.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task RenameFile_Succeeds( ) {
        string path = Path.Combine(
            _tempDir,
            "old.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "data",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize( new RenameFileParameters { Path = path, NewName = "new.txt" } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( File.Exists( path ) );
        Assert.IsTrue( File.Exists( Path.Combine( _tempDir, "new.txt" ) ) );
    }

    /// <summary>
    /// Verifies that renaming a directory succeeds, removes the old directory,
    /// and creates the directory under the new name.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task RenameDirectory_Succeeds( ) {
        string dir = Path.Combine(
            _tempDir,
            "oldDir"
        );
        _ = Directory.CreateDirectory( dir );

        JsonElement parameters = Serialize( new RenameFileParameters { Path = dir, NewName = "newDir" } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsFalse( Directory.Exists( dir ) );
        Assert.IsTrue( Directory.Exists( Path.Combine( _tempDir, "newDir" ) ) );
    }

    /// <summary>
    /// Verifies that renaming a non-existent source returns a failure result with a non-null exception.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task RenameNonExistent_ReturnsFailure( ) {
        string path = Path.Combine(
            _tempDir,
            "missing.txt"
        );

        JsonElement parameters = Serialize( new RenameFileParameters { Path = path, NewName = "new.txt" } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }

    /// <summary>
    /// Verifies that when the destination name already exists and
    /// <c>Overwrite</c> is <see langword="false"/>, the handler returns
    /// a failure result.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task RenameFile_DestinationExists_OverwriteFalse_ReturnsFailure( ) {
        string path = Path.Combine(
            _tempDir,
            "old.txt"
        );
        string existing = Path.Combine(
            _tempDir,
            "new.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "data",
            TestContext.CancellationToken
        );
        await File.WriteAllTextAsync(
            existing,
            "existing",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize(
            new RenameFileParameters { Path = path, NewName = "new.txt", Overwrite = false }
        );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }

    /// <summary>
    /// Verifies that when the destination name already exists and
    /// <c>Overwrite</c> is <see langword="true"/>, the handler succeeds and
    /// the destination contains the content from the original source file.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task RenameFile_DestinationExists_OverwriteTrue_Succeeds( ) {
        string path = Path.Combine(
            _tempDir,
            "old.txt"
        );
        string existing = Path.Combine(
            _tempDir,
            "new.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "newdata",
            TestContext.CancellationToken
        );
        await File.WriteAllTextAsync(
            existing,
            "existing",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize(
            new RenameFileParameters { Path = path, NewName = "new.txt", Overwrite = true }
        );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual(
            "newdata",
            await File.ReadAllTextAsync(
                Path.Combine( _tempDir, "new.txt" ),
                TestContext.CancellationToken
            )
        );
    }
}
