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
/// Unit tests for the <see cref="GetFileInfoHandler"/> action handler.
/// Validates returning metadata for files, directories, and non-existent paths.
/// </summary>
[TestClass]
public class GetFileInfoHandlerTests {

    /// <summary>
    /// Temporary directory created for each test and cleaned up afterward.
    /// </summary>
    private string _tempDir = null!;
    /// <summary>
    /// The handler instance under test.
    /// </summary>
    private GetFileInfoHandler _handler = null!;
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
        _handler = new GetFileInfoHandler(
            TestFilePathResolver.AllowAll,
            NullLogger<GetFileInfoHandler>.Instance
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
    /// Verifies that file metadata is returned correctly for an existing file.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task GetFileInfo_ExistingFile_ReturnsMetadata( ) {
        string filePath = Path.Combine( _tempDir, "test.txt" );
        await File.WriteAllTextAsync( filePath, "hello world", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new GetFileInfoParameters { Path = filePath } );
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
        JsonDocument doc = JsonDocument.Parse( messages[0].Message );
        Assert.IsTrue( doc.RootElement.GetProperty( "exists" ).GetBoolean( ) );
        Assert.IsFalse( doc.RootElement.GetProperty( "isDirectory" ).GetBoolean( ) );
        Assert.IsGreaterThan( 0L, doc.RootElement.GetProperty( "size" ).GetInt64( ) );
    }

    /// <summary>
    /// Verifies that directory metadata is returned correctly for an existing directory.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task GetFileInfo_ExistingDirectory_ReturnsMetadata( ) {
        string dirPath = Path.Combine( _tempDir, "subdir" );
        _ = Directory.CreateDirectory( dirPath );

        JsonElement parameters = Serialize( new GetFileInfoParameters { Path = dirPath } );
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
        JsonDocument doc = JsonDocument.Parse( messages[0].Message );
        Assert.IsTrue( doc.RootElement.GetProperty( "exists" ).GetBoolean( ) );
        Assert.IsTrue( doc.RootElement.GetProperty( "isDirectory" ).GetBoolean( ) );
    }

    /// <summary>
    /// Verifies that a non-existent path returns exists=false and action succeeds.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task GetFileInfo_NonExistentPath_ReturnsNotFound( ) {
        string missingPath = Path.Combine( _tempDir, "does-not-exist.txt" );

        JsonElement parameters = Serialize( new GetFileInfoParameters { Path = missingPath } );
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
        JsonDocument doc = JsonDocument.Parse( messages[0].Message );
        Assert.IsFalse( doc.RootElement.GetProperty( "exists" ).GetBoolean( ) );
    }

    /// <summary>
    /// Verifies that a denied path returns failure with UnauthorizedAccessException.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task GetFileInfo_DeniedPath_ReturnsFailure( ) {
        GetFileInfoHandler denied = new(
            TestFilePathResolver.DenyAll,
            NullLogger<GetFileInfoHandler>.Instance
        );

        JsonElement parameters = Serialize( new GetFileInfoParameters { Path = "/some/path" } );
        ActionOperatorResult result = await denied.ExecuteAsync(
            parameters,
            _channel.Writer,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }
}
