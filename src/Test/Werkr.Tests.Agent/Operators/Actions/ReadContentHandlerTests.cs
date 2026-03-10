using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Operators.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="ReadContentHandler"/> action handler.
/// Validates reading file content, truncation via MaxBytes, encoding, and edge cases.
/// </summary>
[TestClass]
public class ReadContentHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private ReadContentHandler _handler = null!;
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
        _handler = new ReadContentHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<ReadContentHandler>.Instance
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
    /// Verifies that reading a UTF-8 file returns its full content.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ReadContent_Utf8File_ReturnsContent( ) {
        string filePath = Path.Combine( _tempDir, "test.txt" );
        await File.WriteAllTextAsync( filePath, "hello world", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new ReadContentParameters { Path = filePath } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );

        _channel.Writer.Complete( );
        List<OperatorOutput> messages = [ ];
        await foreach (OperatorOutput msg in _channel.Reader.ReadAllAsync( TestContext.CancellationToken )) {
            messages.Add( msg );
        }

        Assert.HasCount( 1, messages );
        Assert.AreEqual( "hello world", messages[0].Message );
    }

    /// <summary>
    /// Verifies that MaxBytes truncates the output content.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ReadContent_MaxBytes_TruncatesContent( ) {
        string filePath = Path.Combine( _tempDir, "test.txt" );
        await File.WriteAllTextAsync( filePath, "abcdefghijklmnop", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new ReadContentParameters { Path = filePath, MaxBytes = 5 } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );

        _channel.Writer.Complete( );
        List<OperatorOutput> messages = [ ];
        await foreach (OperatorOutput msg in _channel.Reader.ReadAllAsync( TestContext.CancellationToken )) {
            messages.Add( msg );
        }

        Assert.HasCount( 1, messages );
        Assert.AreEqual( "abcde", messages[0].Message );
    }

    /// <summary>
    /// Verifies that reading a non-existent file returns failure.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ReadContent_FileNotFound_ReturnsFailure( ) {
        string missingPath = Path.Combine( _tempDir, "missing.txt" );

        JsonElement parameters = Serialize( new ReadContentParameters { Path = missingPath } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<FileNotFoundException>( result.Exception );
    }

    /// <summary>
    /// Verifies that reading an empty file succeeds with empty content.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ReadContent_EmptyFile_ReturnsEmptyContent( ) {
        string filePath = Path.Combine( _tempDir, "empty.txt" );
        await File.WriteAllTextAsync( filePath, string.Empty, TestContext.CancellationToken );

        JsonElement parameters = Serialize( new ReadContentParameters { Path = filePath } );
        ActionOperatorResult result = await _handler.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
    }

    /// <summary>
    /// Verifies that a denied path returns failure with UnauthorizedAccessException.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task ReadContent_DeniedPath_ReturnsFailure( ) {
        ReadContentHandler denied = new(
            TestFilePathResolver.DenyAll,
            NullLogger<ReadContentHandler>.Instance
        );

        JsonElement parameters = Serialize( new ReadContentParameters { Path = "/some/path" } );
        ActionOperatorResult result = await denied.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }
}
