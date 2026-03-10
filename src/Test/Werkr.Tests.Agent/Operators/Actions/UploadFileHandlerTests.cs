using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Operators.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="UploadFileHandler"/> action handler.
/// Uses <see cref="MockHttpMessageHandler"/> to avoid real HTTP traffic.
/// </summary>
[TestClass]
public class UploadFileHandlerTests {

    /// <summary>Temporary directory for source file tests.</summary>
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

    private static UploadFileHandler CreateHandler(
        MockHttpMessageHandler mockHttp,
        bool allowUrls = true,
        bool allowFiles = true
    ) =>
        new(
            allowUrls ? TestUrlValidator.AllowAll : TestUrlValidator.DenyAll,
            new TestHttpClientFactory( mockHttp ),
            allowFiles ? TestFilePathResolver.AllowAll : TestFilePathResolver.DenyAll,
            NullLogger<UploadFileHandler>.Instance
        );

    /// <summary>Verifies a file upload sends multipart form data.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task UploadFile_Success_SendsMultipart( ) {
        string? capturedContentType = null;
        MockHttpMessageHandler mock = new( ( req, _ ) => {
            capturedContentType = req.Content?.Headers.ContentType?.MediaType;
            return Task.FromResult( new HttpResponseMessage( HttpStatusCode.OK ) {
                Content = new StringContent( "{\"uploaded\":true}" ),
            } );
        } );
        UploadFileHandler handler = CreateHandler( mock );

        string srcFile = Path.Combine( _tempDir, "upload.txt" );
        await File.WriteAllTextAsync( srcFile, "upload-content", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new UploadFileParameters {
            FilePath = srcFile,
            Url = "https://example.com/upload",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "multipart/form-data", capturedContentType );
    }

    /// <summary>Verifies output variable contains status code and metadata.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task UploadFile_OutputVariable_ContainsMetadata( ) {
        MockHttpMessageHandler mock = new( new HttpResponseMessage( HttpStatusCode.Created ) {
            Content = new StringContent( "{\"id\":99}" ),
        } );
        UploadFileHandler handler = CreateHandler( mock );

        string srcFile = Path.Combine( _tempDir, "data.bin" );
        await File.WriteAllTextAsync( srcFile, "binary-data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new UploadFileParameters {
            FilePath = srcFile,
            Url = "https://example.com/upload",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( result.OutputVariableValue );

        using JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue );
        Assert.AreEqual( 201, doc.RootElement.GetProperty( "statusCode" ).GetInt32( ) );
        Assert.AreEqual( "data.bin", doc.RootElement.GetProperty( "fileName" ).GetString( ) );
        Assert.IsGreaterThan( 0L, doc.RootElement.GetProperty( "fileSize" ).GetInt64( ) );
    }

    /// <summary>Verifies that a missing source file causes failure.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task UploadFile_MissingFile_Fails( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( );
        UploadFileHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new UploadFileParameters {
            FilePath = Path.Combine( _tempDir, "nonexistent.txt" ),
            Url = "https://example.com/upload",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<FileNotFoundException>( result.Exception );
    }

    /// <summary>Verifies that a denied URL causes failure.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task UploadFile_DeniedUrl_Fails( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( );
        UploadFileHandler handler = CreateHandler( mock, allowUrls: false );

        string srcFile = Path.Combine( _tempDir, "file.txt" );
        await File.WriteAllTextAsync( srcFile, "data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new UploadFileParameters {
            FilePath = srcFile,
            Url = "https://example.com/upload",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }

    /// <summary>Verifies that a denied file path causes failure.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task UploadFile_DeniedFilePath_Fails( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( );
        UploadFileHandler handler = CreateHandler( mock, allowFiles: false );

        JsonElement parameters = Serialize( new UploadFileParameters {
            FilePath = "/denied/file.txt",
            Url = "https://example.com/upload",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
    }

    /// <summary>Verifies the custom HTTP method is used.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task UploadFile_CustomMethod_Used( ) {
        string? capturedMethod = null;
        MockHttpMessageHandler mock = new( ( req, _ ) => {
            capturedMethod = req.Method.Method;
            return Task.FromResult( new HttpResponseMessage( HttpStatusCode.OK ) {
                Content = new StringContent( "ok" ),
            } );
        } );
        UploadFileHandler handler = CreateHandler( mock );

        string srcFile = Path.Combine( _tempDir, "file.txt" );
        await File.WriteAllTextAsync( srcFile, "data", TestContext.CancellationToken );

        JsonElement parameters = Serialize( new UploadFileParameters {
            FilePath = srcFile,
            Url = "https://example.com/upload",
            Method = "PUT",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "PUT", capturedMethod );
    }

    /// <summary>Verifies the Action property returns the correct name.</summary>
    [TestMethod]
    public void UploadFile_ActionProperty_IsCorrect( ) {
        UploadFileHandler handler = CreateHandler( MockHttpMessageHandler.Ok( ) );
        Assert.AreEqual( "UploadFile", handler.Action );
    }
}
