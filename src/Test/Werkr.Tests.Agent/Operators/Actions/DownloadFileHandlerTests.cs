using System.Net;
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
/// Unit tests for the <see cref="DownloadFileHandler"/> action handler.
/// Uses <see cref="MockHttpMessageHandler"/> to avoid real HTTP traffic.
/// </summary>
[TestClass]
public class DownloadFileHandlerTests {

    /// <summary>Temporary directory for download destination tests.</summary>
    private string _tempDir = null!;
    /// <summary>Unbounded channel for capturing <see cref="OperatorOutput"/> messages.</summary>
    private Channel<OperatorOutput> _channel = null!;

    /// <summary>Gets or sets the MSTest test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Sets up the temp directory and output channel.</summary>
    [TestInitialize]
    public void TestInit( ) {
        _tempDir = Path.Combine( Path.GetTempPath( ), $"werkr-test-{Guid.NewGuid( )}" );
        _ = Directory.CreateDirectory( _tempDir );
        _channel = Channel.CreateUnbounded<OperatorOutput>( );
    }

    /// <summary>Cleans up the temp directory.</summary>
    [TestCleanup]
    public void TestCleanup( ) {
        if (Directory.Exists( _tempDir )) {
            Directory.Delete( _tempDir, recursive: true );
        }
    }

    private static JsonElement Serialize<T>( T value ) =>
        TestActionDescriptor.Serialize( value );

    private static DownloadFileHandler CreateHandler(
        MockHttpMessageHandler mockHttp,
        bool allowUrls = true,
        bool allowFiles = true
    ) =>
        new(
            allowUrls ? TestUrlValidator.AllowAll : TestUrlValidator.DenyAll,
            new TestHttpClientFactory( mockHttp ),
            allowFiles ? TestFilePathResolver.AllowAll : TestFilePathResolver.DenyAll,
            NullLogger<DownloadFileHandler>.Instance
        );

    /// <summary>Verifies a successful download writes content to the destination file.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DownloadFile_Success_WritesFile( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( "file-content" );
        DownloadFileHandler handler = CreateHandler( mock );

        string dest = Path.Combine( _tempDir, "downloaded.txt" );
        JsonElement parameters = Serialize( new DownloadFileParameters {
            Url = "https://example.com/file.txt",
            Destination = dest,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( dest ) );
        string content = await File.ReadAllTextAsync( dest, TestContext.CancellationToken );
        Assert.AreEqual( "file-content", content );
    }

    /// <summary>Verifies the output variable contains download metadata.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DownloadFile_OutputVariable_ContainsMetadata( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( "data" );
        DownloadFileHandler handler = CreateHandler( mock );

        string dest = Path.Combine( _tempDir, "meta.txt" );
        JsonElement parameters = Serialize( new DownloadFileParameters {
            Url = "https://example.com/file.txt",
            Destination = dest,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( result.OutputVariableValue );

        using JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue );
        Assert.AreEqual( dest, doc.RootElement.GetProperty( "path" ).GetString( ) );
        Assert.IsGreaterThan( 0L, doc.RootElement.GetProperty( "size" ).GetInt64( ) );
    }

    /// <summary>Verifies that existing files are not overwritten when Overwrite is false.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DownloadFile_ExistingFile_NoOverwrite_Fails( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( "new-content" );
        DownloadFileHandler handler = CreateHandler( mock );

        string dest = Path.Combine( _tempDir, "existing.txt" );
        await File.WriteAllTextAsync( dest, "original", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new DownloadFileParameters {
            Url = "https://example.com/file.txt",
            Destination = dest,
            Overwrite = false,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<IOException>( result.Exception );
        string content = await File.ReadAllTextAsync( dest, TestContext.CancellationToken );
        Assert.AreEqual( "original", content );
    }

    /// <summary>Verifies that existing files are overwritten when Overwrite is true.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DownloadFile_ExistingFile_Overwrite_Succeeds( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( "new-content" );
        DownloadFileHandler handler = CreateHandler( mock );

        string dest = Path.Combine( _tempDir, "existing.txt" );
        await File.WriteAllTextAsync( dest, "original", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new DownloadFileParameters {
            Url = "https://example.com/file.txt",
            Destination = dest,
            Overwrite = true,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        string content = await File.ReadAllTextAsync( dest, TestContext.CancellationToken );
        Assert.AreEqual( "new-content", content );
    }

    /// <summary>Verifies that a denied URL causes failure.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DownloadFile_DeniedUrl_Fails( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( );
        DownloadFileHandler handler = CreateHandler( mock, allowUrls: false );

        string dest = Path.Combine( _tempDir, "denied.txt" );
        JsonElement parameters = Serialize( new DownloadFileParameters {
            Url = "https://example.com/file.txt",
            Destination = dest,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }

    /// <summary>Verifies that a denied destination path causes failure.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DownloadFile_DeniedDestination_Fails( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( "data" );
        DownloadFileHandler handler = CreateHandler( mock, allowFiles: false );

        JsonElement parameters = Serialize( new DownloadFileParameters {
            Url = "https://example.com/file.txt",
            Destination = "/denied/file.txt",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }

    /// <summary>Verifies that server errors cause failure.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DownloadFile_ServerError_Fails( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.WithStatus( HttpStatusCode.InternalServerError );
        DownloadFileHandler handler = CreateHandler( mock );

        string dest = Path.Combine( _tempDir, "error.txt" );
        JsonElement parameters = Serialize( new DownloadFileParameters {
            Url = "https://example.com/file.txt",
            Destination = dest,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsFalse( File.Exists( dest ) );
    }

    /// <summary>Verifies the Action property returns the correct name.</summary>
    [TestMethod]
    public void DownloadFile_ActionProperty_IsCorrect( ) {
        DownloadFileHandler handler = CreateHandler( MockHttpMessageHandler.Ok( ) );
        Assert.AreEqual( "DownloadFile", handler.Action );
    }

    /// <summary>Verifies intermediate directories are created.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task DownloadFile_CreatesIntermediateDirectories( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( "nested" );
        DownloadFileHandler handler = CreateHandler( mock );

        string dest = Path.Combine( _tempDir, "sub", "dir", "file.txt" );
        JsonElement parameters = Serialize( new DownloadFileParameters {
            Url = "https://example.com/file.txt",
            Destination = dest,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( dest ) );
        string content = await File.ReadAllTextAsync( dest, TestContext.CancellationToken );
        Assert.AreEqual( "nested", content );
    }
}
