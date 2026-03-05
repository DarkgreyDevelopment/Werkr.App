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
/// Unit tests for the <see cref="CreateFileHandler"/> action handler.
/// Validates creation of empty files, files with content, parent-directory
/// auto-creation, overwrite guards, and failure when parent directories
/// do not exist and auto-creation is disabled.
/// </summary>
[TestClass]
public class CreateFileHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private CreateFileHandler _handler = null!;
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
        _handler = new CreateFileHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<CreateFileHandler>.Instance
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
    /// Verifies that creating a new file with no content produces a zero-length file.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateEmptyFile_Succeeds( ) {
        string path = Path.Combine(
            _tempDir,
            "new.txt"
        );

        JsonElement parameters = Serialize( new CreateFileParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( path ) );
        Assert.AreEqual( 0, new FileInfo( path ).Length );
    }

    /// <summary>
    /// Verifies that a new file is created with the specified text content.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateFileWithContent_Succeeds( ) {
        string path = Path.Combine(
            _tempDir,
            "content.txt"
        );

        JsonElement parameters = Serialize( new CreateFileParameters { Path = path, Content = "hello world" } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual(
            "hello world",
            await File.ReadAllTextAsync(
                path,
                TestContext.CancellationToken
            )
        );
    }

    /// <summary>
    /// Verifies that the handler creates missing parent directories when <c>CreateParentDirectories</c> is enabled.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateFileCreatesParentDirectories( ) {
        string path = Path.Combine(
            _tempDir,
            "sub",
            "deep",
            "file.txt"
        );

        JsonElement parameters = Serialize( new CreateFileParameters { Path = path, CreateParentDirectories = true } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( path ) );
    }

    /// <summary>
    /// Verifies that attempting to create a file that already exists without
    /// the <c>Overwrite</c> flag returns a failure result.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateFile_ExistsNoOverwrite_ReturnsFailure( ) {
        string path = Path.Combine(
            _tempDir,
            "existing.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "data",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize( new CreateFileParameters { Path = path, Overwrite = false } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
    }

    /// <summary>
    /// Verifies that creating a file with the <c>Overwrite</c> flag when the file
    /// already exists replaces the old content with the new content.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateFile_ExistsWithOverwrite_Succeeds( ) {
        string path = Path.Combine(
            _tempDir,
            "existing.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "old",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize(
            new CreateFileParameters { Path = path, Content = "new", Overwrite = true }
        );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual(
            "new",
            await File.ReadAllTextAsync(
                path,
                TestContext.CancellationToken
            )
        );
    }

    /// <summary>
    /// Verifies that when the parent directory does not exist and
    /// <c>CreateParentDirectories</c> is <see langword="false"/>, the handler
    /// returns a failure result with a non-null exception.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task CreateFile_NoParentNoCreate_ReturnsFailure( ) {
        string path = Path.Combine(
            _tempDir,
            "missing-parent",
            "file.txt"
        );

        JsonElement parameters = Serialize(
            new CreateFileParameters { Path = path, CreateParentDirectories = false }
        );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }
}
