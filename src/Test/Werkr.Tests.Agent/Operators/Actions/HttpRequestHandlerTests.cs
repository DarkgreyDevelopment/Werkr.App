using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Operators.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="HttpRequestHandler"/> action handler.
/// Uses <see cref="MockHttpMessageHandler"/> to avoid real HTTP traffic.
/// </summary>
[TestClass]
public class HttpRequestHandlerTests {

    /// <summary>Temporary directory for file output tests.</summary>
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

    private static HttpRequestHandler CreateHandler(
        MockHttpMessageHandler mockHttp,
        bool allowUrls = true,
        bool allowFiles = true
    ) =>
        new(
            allowUrls ? TestUrlValidator.AllowAll : TestUrlValidator.DenyAll,
            new TestHttpClientFactory( mockHttp ),
            allowFiles ? TestFilePathResolver.AllowAll : TestFilePathResolver.DenyAll,
            Options.Create( new WorkflowVariableOptions( ) ),
            NullLogger<HttpRequestHandler>.Instance
        );

    /// <summary>Verifies a simple GET request returns the response body.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task HttpRequest_Get_ReturnsBody( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( "hello" );
        HttpRequestHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new HttpRequestParameters { Url = "https://example.com/api" } );
        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( result.OutputVariableValue );

        using JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue );
        Assert.AreEqual( 200, doc.RootElement.GetProperty( "statusCode" ).GetInt32( ) );
        Assert.AreEqual( "hello", doc.RootElement.GetProperty( "body" ).GetString( ) );
    }

    /// <summary>Verifies POST sends the parameter body.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task HttpRequest_PostWithBody_SendsBody( ) {
        string? capturedBody = null;
        MockHttpMessageHandler mock = new( async ( req, _ ) => {
            capturedBody = await req.Content!.ReadAsStringAsync( CancellationToken.None );
            return new HttpResponseMessage( HttpStatusCode.OK ) {
                Content = new StringContent( "ok" ),
            };
        } );
        HttpRequestHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new HttpRequestParameters {
            Url = "https://example.com/api",
            Method = "POST",
            Body = "{\"key\":\"value\"}",
            ContentType = "application/json",
        } );
        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "{\"key\":\"value\"}", capturedBody );
    }

    /// <summary>Verifies input variable is used as body when Body parameter is null.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task HttpRequest_PostWithInputVariable_UsesVariableAsBody( ) {
        string? capturedBody = null;
        MockHttpMessageHandler mock = new( async ( req, _ ) => {
            capturedBody = await req.Content!.ReadAsStringAsync( CancellationToken.None );
            return new HttpResponseMessage( HttpStatusCode.OK ) {
                Content = new StringContent( "ok" ),
            };
        } );
        HttpRequestHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new HttpRequestParameters {
            Url = "https://example.com/api",
            Method = "POST",
        } );
        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: "from-variable",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "from-variable", capturedBody );
    }

    /// <summary>Verifies that Body parameter takes precedence over input variable.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task HttpRequest_BodyParamPrecedence_OverInputVariable( ) {
        string? capturedBody = null;
        MockHttpMessageHandler mock = new( async ( req, _ ) => {
            capturedBody = await req.Content!.ReadAsStringAsync( CancellationToken.None );
            return new HttpResponseMessage( HttpStatusCode.OK ) {
                Content = new StringContent( "ok" ),
            };
        } );
        HttpRequestHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new HttpRequestParameters {
            Url = "https://example.com/api",
            Method = "POST",
            Body = "param-body",
        } );
        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: "variable-body",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "param-body", capturedBody );
    }

    /// <summary>Verifies that unexpected status codes cause failure.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task HttpRequest_UnexpectedStatusCode_Fails( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.WithStatus( HttpStatusCode.NotFound, "not found" );
        HttpRequestHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new HttpRequestParameters {
            Url = "https://example.com/api",
            ExpectedStatusCodes = [200],
        } );
        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }

    /// <summary>Verifies that custom expected status codes are accepted.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task HttpRequest_CustomExpectedStatusCode_Accepted( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.WithStatus( HttpStatusCode.Created );
        HttpRequestHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new HttpRequestParameters {
            Url = "https://example.com/api",
            ExpectedStatusCodes = [201],
        } );
        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
    }

    /// <summary>Verifies response body is written to OutputFilePath.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task HttpRequest_OutputFilePath_WritesToFile( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( "file-content" );
        HttpRequestHandler handler = CreateHandler( mock );

        string outPath = Path.Combine( _tempDir, "response.txt" );
        JsonElement parameters = Serialize( new HttpRequestParameters {
            Url = "https://example.com/api",
            OutputFilePath = outPath,
        } );
        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsTrue( File.Exists( outPath ) );
        string content = await File.ReadAllTextAsync( outPath, TestContext.CancellationToken );
        Assert.AreEqual( "file-content", content );
    }

    /// <summary>Verifies that a denied URL causes failure.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task HttpRequest_DeniedUrl_Fails( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( );
        HttpRequestHandler handler = CreateHandler( mock, allowUrls: false );

        JsonElement parameters = Serialize( new HttpRequestParameters { Url = "https://example.com/api" } );
        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }

    /// <summary>Verifies that custom headers are sent with the request.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task HttpRequest_CustomHeaders_Sent( ) {
        string? authHeader = null;
        MockHttpMessageHandler mock = new( ( req, _ ) => {
            authHeader = req.Headers.Authorization?.ToString( );
            return Task.FromResult( new HttpResponseMessage( HttpStatusCode.OK ) {
                Content = new StringContent( "ok" ),
            } );
        } );
        HttpRequestHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new HttpRequestParameters {
            Url = "https://example.com/api",
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer test-token" },
        } );
        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "Bearer test-token", authHeader );
    }

    /// <summary>Verifies the Action property returns the correct name.</summary>
    [TestMethod]
    public void HttpRequest_ActionProperty_IsCorrect( ) {
        HttpRequestHandler handler = CreateHandler( MockHttpMessageHandler.Ok( ) );
        Assert.AreEqual( "HttpRequest", handler.Action );
    }
}
