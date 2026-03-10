using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Operators.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="SendWebhookHandler"/> action handler.
/// Uses <see cref="MockHttpMessageHandler"/> to avoid real HTTP traffic.
/// </summary>
[TestClass]
public class SendWebhookHandlerTests {

    /// <summary>Unbounded channel for capturing <see cref="OperatorOutput"/> messages.</summary>
    private Channel<OperatorOutput> _channel = null!;

    /// <summary>Gets or sets the MSTest test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Sets up the output channel.</summary>
    [TestInitialize]
    public void TestInit( ) {
        _channel = Channel.CreateUnbounded<OperatorOutput>( );
    }

    private static JsonElement Serialize<T>( T value ) =>
        TestActionDescriptor.Serialize( value );

    private static SendWebhookHandler CreateHandler(
        MockHttpMessageHandler mockHttp,
        bool allowUrls = true
    ) =>
        new(
            allowUrls ? TestUrlValidator.AllowAll : TestUrlValidator.DenyAll,
            new TestHttpClientFactory( mockHttp ),
            NullLogger<SendWebhookHandler>.Instance
        );

    /// <summary>Verifies a basic webhook POST is sent with payload from parameter.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task SendWebhook_PayloadFromParameter_SendsPost( ) {
        string? capturedBody = null;
        string? capturedContentType = null;
        MockHttpMessageHandler mock = new( async ( req, _ ) => {
            capturedBody = await req.Content!.ReadAsStringAsync( CancellationToken.None );
            capturedContentType = req.Content.Headers.ContentType?.MediaType;
            return new HttpResponseMessage( HttpStatusCode.OK ) {
                Content = new StringContent( "{\"ok\":true}" ),
            };
        } );
        SendWebhookHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new SendWebhookParameters {
            Url = "https://example.com/webhook",
            Payload = "{\"event\":\"test\"}",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "{\"event\":\"test\"}", capturedBody );
        Assert.AreEqual( "application/json", capturedContentType );
    }

    /// <summary>Verifies input variable value is used as payload when Payload is null.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task SendWebhook_PayloadFromVariable_SendsPost( ) {
        string? capturedBody = null;
        MockHttpMessageHandler mock = new( async ( req, _ ) => {
            capturedBody = await req.Content!.ReadAsStringAsync( CancellationToken.None );
            return new HttpResponseMessage( HttpStatusCode.OK ) {
                Content = new StringContent( "ok" ),
            };
        } );
        SendWebhookHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new SendWebhookParameters {
            Url = "https://example.com/webhook",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: "{\"from\":\"variable\"}",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "{\"from\":\"variable\"}", capturedBody );
    }

    /// <summary>Verifies Payload parameter takes precedence over input variable.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task SendWebhook_PayloadPrecedence_OverVariable( ) {
        string? capturedBody = null;
        MockHttpMessageHandler mock = new( async ( req, _ ) => {
            capturedBody = await req.Content!.ReadAsStringAsync( CancellationToken.None );
            return new HttpResponseMessage( HttpStatusCode.OK ) {
                Content = new StringContent( "ok" ),
            };
        } );
        SendWebhookHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new SendWebhookParameters {
            Url = "https://example.com/webhook",
            Payload = "{\"from\":\"param\"}",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: "{\"from\":\"variable\"}",
            cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "{\"from\":\"param\"}", capturedBody );
    }

    /// <summary>Verifies default empty body when no payload or variable.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task SendWebhook_NoPayloadNoVariable_SendsEmptyObject( ) {
        string? capturedBody = null;
        MockHttpMessageHandler mock = new( async ( req, _ ) => {
            capturedBody = await req.Content!.ReadAsStringAsync( CancellationToken.None );
            return new HttpResponseMessage( HttpStatusCode.OK ) {
                Content = new StringContent( "ok" ),
            };
        } );
        SendWebhookHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new SendWebhookParameters {
            Url = "https://example.com/webhook",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "{}", capturedBody );
    }

    /// <summary>Verifies output variable contains statusCode and responseBody.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task SendWebhook_OutputVariable_ContainsMetadata( ) {
        MockHttpMessageHandler mock = new( new HttpResponseMessage( HttpStatusCode.Accepted ) {
            Content = new StringContent( "{\"id\":42}" ),
        } );
        SendWebhookHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new SendWebhookParameters {
            Url = "https://example.com/webhook",
            Payload = "{}",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( result.OutputVariableValue );

        using JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue );
        Assert.AreEqual( 202, doc.RootElement.GetProperty( "statusCode" ).GetInt32( ) );
        Assert.AreEqual( "{\"id\":42}", doc.RootElement.GetProperty( "responseBody" ).GetString( ) );
        Assert.IsGreaterThanOrEqualTo( 0L, doc.RootElement.GetProperty( "elapsedMs" ).GetInt64( ) );
    }

    /// <summary>Verifies that custom headers are sent with the webhook request.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task SendWebhook_CustomHeaders_Sent( ) {
        string? customHeader = null;
        MockHttpMessageHandler mock = new( ( req, _ ) => {
            customHeader = req.Headers.TryGetValues( "X-Custom", out IEnumerable<string>? vals )
                ? string.Join( ",", vals )
                : null;
            return Task.FromResult( new HttpResponseMessage( HttpStatusCode.OK ) {
                Content = new StringContent( "ok" ),
            } );
        } );
        SendWebhookHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new SendWebhookParameters {
            Url = "https://example.com/webhook",
            Payload = "{}",
            Headers = new Dictionary<string, string> { ["X-Custom"] = "test-value" },
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.AreEqual( "test-value", customHeader );
    }

    /// <summary>Verifies denied URL causes failure.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task SendWebhook_DeniedUrl_Fails( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( );
        SendWebhookHandler handler = CreateHandler( mock, allowUrls: false );

        JsonElement parameters = Serialize( new SendWebhookParameters {
            Url = "https://example.com/webhook",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }

    /// <summary>Verifies the Action property returns the correct name.</summary>
    [TestMethod]
    public void SendWebhook_ActionProperty_IsCorrect( ) {
        SendWebhookHandler handler = CreateHandler( MockHttpMessageHandler.Ok( ) );
        Assert.AreEqual( "SendWebhook", handler.Action );
    }
}
