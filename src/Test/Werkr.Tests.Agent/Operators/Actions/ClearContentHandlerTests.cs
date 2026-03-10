using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Operators.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="ClearContentHandler"/> action handler.
/// Validates that the handler truncates an existing file to zero bytes,
/// returns failure when the target file does not exist, and succeeds
/// when the file is already empty.
/// </summary>
[TestClass]
public class ClearContentHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private ClearContentHandler _handler = null!;
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
        _handler = new ClearContentHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<ClearContentHandler>.Instance
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
    /// Verifies that clearing a file with existing content succeeds and leaves the file empty.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ClearContent_Succeeds( ) {
        string path = Path.Combine(
            _tempDir,
            "file.txt"
        );
        await File.WriteAllTextAsync(
            path,
            "hello world",
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize( new ClearContentParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.AreEqual(
            string.Empty,
            await File.ReadAllTextAsync(
                path,
                TestContext.CancellationToken
            )
        );
    }

    /// <summary>
    /// Verifies that attempting to clear a non-existent file returns a failure result
    /// with a <see cref="FileNotFoundException"/> in the exception property.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ClearContent_FileNotFound_ReturnsFailure( ) {
        string path = Path.Combine(
            _tempDir,
            "missing.txt"
        );

        JsonElement parameters = Serialize( new ClearContentParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        _ = Assert.IsInstanceOfType<FileNotFoundException>( result.Exception );
    }

    /// <summary>
    /// Verifies that clearing an already-empty file still returns a success result.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ClearContent_AlreadyEmpty_StillSucceeds( ) {
        string path = Path.Combine(
            _tempDir,
            "empty.txt"
        );
        await File.WriteAllTextAsync(
            path,
            string.Empty,
            TestContext.CancellationToken
        );

        JsonElement parameters = Serialize( new ClearContentParameters { Path = path } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }
}
