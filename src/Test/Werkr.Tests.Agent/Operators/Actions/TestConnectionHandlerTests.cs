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
/// Unit tests for the <see cref="TestConnectionHandler"/> action handler.
/// Tests TCP and HTTP connectivity checking with mock infrastructure.
/// </summary>
[TestClass]
public class TestConnectionHandlerTests {

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

    private static TestConnectionHandler CreateHandler(
        MockHttpMessageHandler mockHttp,
        bool allowUrls = true
    ) =>
        new(
            allowUrls ? TestUrlValidator.AllowAll : TestUrlValidator.DenyAll,
            new TestHttpClientFactory( mockHttp ),
            NullLogger<TestConnectionHandler>.Instance
        );

    /// <summary>Verifies HTTP mode returns reachable when server responds with 200.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task TestConnection_Http_Reachable( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( );
        TestConnectionHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new TestConnectionParameters {
            Host = "example.com",
            Port = 80,
            Protocol = ConnectionProtocol.Http,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( result.OutputVariableValue );

        using JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue );
        Assert.IsTrue( doc.RootElement.GetProperty( "reachable" ).GetBoolean( ) );
        Assert.AreEqual( 200, doc.RootElement.GetProperty( "statusCode" ).GetInt32( ) );
    }

    /// <summary>Verifies HTTPS mode returns reachable with status code.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task TestConnection_Https_Reachable( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( );
        TestConnectionHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new TestConnectionParameters {
            Host = "example.com",
            Port = 443,
            Protocol = ConnectionProtocol.Https,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        using JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.IsTrue( doc.RootElement.GetProperty( "reachable" ).GetBoolean( ) );
    }

    /// <summary>Verifies HTTP mode with expected status code mismatch sets reachable=false.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task TestConnection_Http_ExpectedStatusMismatch_NotReachable( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.WithStatus( HttpStatusCode.NotFound );
        TestConnectionHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new TestConnectionParameters {
            Host = "example.com",
            Port = 80,
            Protocol = ConnectionProtocol.Http,
            ExpectedStatusCode = 200,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success ); // action itself succeeds
        using JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.IsFalse( doc.RootElement.GetProperty( "reachable" ).GetBoolean( ) );
        Assert.AreEqual( 404, doc.RootElement.GetProperty( "statusCode" ).GetInt32( ) );
    }

    /// <summary>Verifies HTTP mode with matching expected status code sets reachable=true.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task TestConnection_Http_ExpectedStatusMatch_Reachable( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.WithStatus( HttpStatusCode.NotFound );
        TestConnectionHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new TestConnectionParameters {
            Host = "example.com",
            Port = 80,
            Protocol = ConnectionProtocol.Http,
            ExpectedStatusCode = 404,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        using JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.IsTrue( doc.RootElement.GetProperty( "reachable" ).GetBoolean( ) );
    }

    /// <summary>Verifies that HTTP mode captures connection errors.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task TestConnection_Http_ConnectionError_NotReachable( ) {
        MockHttpMessageHandler mock = new( ( _, _ ) =>
            throw new HttpRequestException( "Connection refused" ) );
        TestConnectionHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new TestConnectionParameters {
            Host = "example.com",
            Port = 80,
            Protocol = ConnectionProtocol.Http,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success ); // action itself succeeds
        using JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.IsFalse( doc.RootElement.GetProperty( "reachable" ).GetBoolean( ) );
        Assert.IsNotNull( doc.RootElement.GetProperty( "error" ).GetString( ) );
    }

    /// <summary>Verifies denied URL causes action failure.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task TestConnection_DeniedUrl_Fails( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( );
        TestConnectionHandler handler = CreateHandler( mock, allowUrls: false );

        JsonElement parameters = Serialize( new TestConnectionParameters {
            Host = "example.com",
            Port = 80,
            Protocol = ConnectionProtocol.Http,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }

    /// <summary>Verifies the output contains timing information.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task TestConnection_OutputContainsElapsedMs( ) {
        MockHttpMessageHandler mock = MockHttpMessageHandler.Ok( );
        TestConnectionHandler handler = CreateHandler( mock );

        JsonElement parameters = Serialize( new TestConnectionParameters {
            Host = "example.com",
            Port = 443,
            Protocol = ConnectionProtocol.Https,
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        using JsonDocument doc = JsonDocument.Parse( result.OutputVariableValue! );
        Assert.IsTrue( doc.RootElement.TryGetProperty( "elapsedMs", out JsonElement elapsed ) );
        Assert.IsGreaterThanOrEqualTo( 0L, elapsed.GetInt64( ) );
    }

    /// <summary>Verifies the Action property returns the correct name.</summary>
    [TestMethod]
    public void TestConnection_ActionProperty_IsCorrect( ) {
        TestConnectionHandler handler = CreateHandler( MockHttpMessageHandler.Ok( ) );
        Assert.AreEqual( "TestConnection", handler.Action );
    }
}
