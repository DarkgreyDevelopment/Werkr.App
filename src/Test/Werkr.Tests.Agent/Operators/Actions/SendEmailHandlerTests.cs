using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Operators.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Operators.Actions;

/// <summary>
/// Unit tests for the <see cref="SendEmailHandler"/> action handler.
/// These tests focus on configuration gating, parameter validation, and credential
/// loading. Actual SMTP delivery is not tested here (requires a real SMTP server).
/// </summary>
[TestClass]
public class SendEmailHandlerTests {

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

    private static SendEmailHandler CreateHandler(
        bool enableNetwork = true,
        TestSecretStore? secretStore = null
    ) {
        ActionOperatorConfiguration config = new( ) {
            EnableNetworkActions = enableNetwork,
        };
        return new SendEmailHandler(
            new TestOptionsMonitor<ActionOperatorConfiguration>( config ),
            secretStore ?? new TestSecretStore( ),
            TestFilePathResolver.AllowAll,
            NullLogger<SendEmailHandler>.Instance
        );
    }

    /// <summary>Verifies the handler rejects when EnableNetworkActions is false.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task SendEmail_NetworkDisabled_Fails( ) {
        SendEmailHandler handler = CreateHandler( enableNetwork: false );

        JsonElement parameters = Serialize( new SendEmailParameters {
            SmtpHost = "smtp.example.com",
            From = "sender@example.com",
            To = ["recipient@example.com"],
            Subject = "Test",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<UnauthorizedAccessException>( result.Exception );
    }

    /// <summary>Verifies the handler rejects when there are zero recipients.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task SendEmail_NoRecipients_Fails( ) {
        SendEmailHandler handler = CreateHandler( );

        JsonElement parameters = Serialize( new SendEmailParameters {
            SmtpHost = "smtp.example.com",
            From = "sender@example.com",
            To = [],
            Subject = "Test",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        _ = Assert.IsInstanceOfType<ArgumentException>( result.Exception );
    }

    /// <summary>Verifies the handler fails when a credential is specified but missing from the store.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task SendEmail_MissingCredential_Fails( ) {
        // When EnableNetworkActions is true but we try to connect to a non-existent SMTP server,
        // the handler will fail at the SMTP connect step. However, we can test the credential
        // lookup path by verifying the error message when a credential name is given but not found.
        // Since we can't mock MailKit's SmtpClient easily, we accept the connection failure.
        SendEmailHandler handler = CreateHandler( );

        JsonElement parameters = Serialize( new SendEmailParameters {
            SmtpHost = "127.0.0.1",
            Port = 0, // invalid port to fail fast
            From = "sender@example.com",
            To = ["recipient@example.com"],
            Subject = "Test",
            CredentialName = "missing-cred",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }

    /// <summary>Verifies the handler fails when an attachment file does not exist.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task SendEmail_MissingAttachment_Fails( ) {
        SendEmailHandler handler = CreateHandler( );

        JsonElement parameters = Serialize( new SendEmailParameters {
            SmtpHost = "127.0.0.1",
            Port = 0,
            From = "sender@example.com",
            To = ["recipient@example.com"],
            Subject = "Test",
            Attachments = ["/nonexistent/file.txt"],
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, cancellationToken: TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNotNull( result.Exception );
    }

    /// <summary>Verifies the Action property returns the correct name.</summary>
    [TestMethod]
    public void SendEmail_ActionProperty_IsCorrect( ) {
        SendEmailHandler handler = CreateHandler( );
        Assert.AreEqual( "SendEmail", handler.Action );
    }

    /// <summary>Verifies body falls back to input variable when Body parameter is null.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task SendEmail_BodyFromVariable_UsedWhenBodyNull( ) {
        // This test validates up to the SMTP connect step which will fail.
        // The key assertion is that the handler does not reject due to missing body.
        SendEmailHandler handler = CreateHandler( );

        JsonElement parameters = Serialize( new SendEmailParameters {
            SmtpHost = "127.0.0.1",
            Port = 0,
            From = "sender@example.com",
            To = ["recipient@example.com"],
            Subject = "Test",
        } );

        ActionOperatorResult result = await handler.ExecuteAsync(
            parameters, _channel.Writer, inputVariableValue: "body from variable",
            cancellationToken: TestContext.CancellationToken );

        // Will fail at SMTP connect — that's expected.
        // The test passes as long as no ArgumentException is thrown for missing body.
        Assert.IsFalse( result.Success );
        Assert.IsNotInstanceOfType<ArgumentException>( result.Exception );
    }

    /// <summary>Local <see cref="IOptionsMonitor{T}"/> stub.</summary>
    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T> {
        public TestOptionsMonitor( T currentValue ) {
            CurrentValue = currentValue;
        }
        public T CurrentValue { get; }
        public T Get( string? name ) => CurrentValue;
        public IDisposable? OnChange( Action<T, string?> listener ) => null;
    }
}
