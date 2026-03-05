using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Werkr.Agent.Operators;
using Werkr.Agent.Protos;
using Werkr.Agent.Services;
using Werkr.Common.Configuration;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Cryptography;
using Werkr.Data.Entities.Registration;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Services;

/// <summary>
/// Unit tests for the <see cref="PwshService"/> gRPC service. Validates that a valid encrypted <see cref="ShellRequest"/> produces encrypted <see cref="GrpcLogMsg"/> output, that the service rejects requests when PowerShell is disabled (<see cref="StatusCode.Unimplemented"/>), and that a missing <see cref="RegisteredConnection"/> results in <see cref="StatusCode.Internal"/>.
/// </summary>
[TestClass]
public class PwshServiceTests {
    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for cancellation token access.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Sends a valid encrypted command, verifies the service writes encrypted output, and asserts the decrypted stream contains the expected command output.
    /// </summary>
    [TestMethod]
    public async Task RunCommand_ValidEncryptedRequest_WritesEncryptedOutput( ) {
        byte[] sharedKey = EncryptionProvider.GenerateRandomBytes( 32 );
        RegisteredConnection connection = CreateConnection( sharedKey );
        string keyId = connection.Id.ToString( );

        PwshService service = new(
            new PwshOperator( Options.Create( new AgentSettings { EnablePowerShell = true } ), NullLogger<PwshOperator>.Instance ),
            Options.Create( new AgentSettings { EnablePowerShell = true } ),
            NullLogger<PwshService>.Instance );

        ShellRequest innerRequest = new( ) { Command = "Write-Output 'pwsh-service-test'" };
        EncryptedEnvelope request = PayloadEncryptor.EncryptToEnvelope( innerRequest, sharedKey, keyId );

        MockServerStreamWriter<EncryptedEnvelope> stream = new( );
        TestServerCallContext context = TestServerCallContext.Create( cancellationToken: TestContext.CancellationToken );
        context.ExposedUserState["Connection"] = connection;

        await service.RunCommand( request, stream, context );

        List<string> decryptedMessages = [.. stream.Messages.Select( envelope => {
            GrpcLogMsg logMsg = PayloadEncryptor.DecryptFromEnvelope<GrpcLogMsg>( envelope, sharedKey );
            return logMsg.Message;
        } )];

        Assert.AreNotEqual( -1, decryptedMessages.FindIndex( m => m.Contains( "pwsh-service-test", StringComparison.OrdinalIgnoreCase ) ) );
    }

    /// <summary>
    /// Verifies that invoking <see cref="PwshService.RunCommand"/> when PowerShell is disabled throws an <see cref="RpcException"/> with <see cref="StatusCode.Unimplemented"/>.
    /// </summary>
    [TestMethod]
    public async Task RunCommand_WhenPowerShellDisabled_ThrowsUnimplemented( ) {
        byte[] sharedKey = EncryptionProvider.GenerateRandomBytes( 32 );
        RegisteredConnection connection = CreateConnection( sharedKey );
        string keyId = connection.Id.ToString( );

        PwshService service = new(
            new PwshOperator( Options.Create( new AgentSettings { EnablePowerShell = false } ), NullLogger<PwshOperator>.Instance ),
            Options.Create( new AgentSettings { EnablePowerShell = false } ),
            NullLogger<PwshService>.Instance );

        ShellRequest innerRequest = new( ) { Command = "Write-Output 'x'" };
        EncryptedEnvelope request = PayloadEncryptor.EncryptToEnvelope( innerRequest, sharedKey, keyId );

        MockServerStreamWriter<EncryptedEnvelope> stream = new( );
        TestServerCallContext context = TestServerCallContext.Create( cancellationToken: TestContext.CancellationToken );
        context.ExposedUserState["Connection"] = connection;

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await service.RunCommand( request, stream, context ) );

        Assert.AreEqual( StatusCode.Unimplemented, ex.StatusCode );
    }

    /// <summary>
    /// Verifies that calling <see cref="PwshService.RunCommand"/> without a <see cref="RegisteredConnection"/> in the <see cref="ServerCallContext"/> user state throws an <see cref="RpcException"/> with <see cref="StatusCode.Internal"/>.
    /// </summary>
    [TestMethod]
    public async Task RunCommand_MissingConnection_ThrowsInternal( ) {
        byte[] sharedKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string keyId = Guid.NewGuid( ).ToString( );

        PwshService service = new(
            new PwshOperator( Options.Create( new AgentSettings { EnablePowerShell = true } ), NullLogger<PwshOperator>.Instance ),
            Options.Create( new AgentSettings { EnablePowerShell = true } ),
            NullLogger<PwshService>.Instance );

        ShellRequest innerRequest = new( ) { Command = "Write-Output 'x'" };
        EncryptedEnvelope request = PayloadEncryptor.EncryptToEnvelope( innerRequest, sharedKey, keyId );

        MockServerStreamWriter<EncryptedEnvelope> stream = new( );
        TestServerCallContext context = TestServerCallContext.Create( cancellationToken: TestContext.CancellationToken );

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await service.RunCommand( request, stream, context ) );

        Assert.AreEqual( StatusCode.Internal, ex.StatusCode );
    }

    /// <summary>
    /// Creates a <see cref="RegisteredConnection"/> entity pre-loaded with the supplied shared key.
    /// </summary>
    private static RegisteredConnection CreateConnection( byte[] sharedKey ) {
        return new RegisteredConnection {
            Id = Guid.NewGuid( ),
            ConnectionName = "Agent",
            RemoteUrl = "https://localhost:5100",
            OutboundApiKey = "outbound",
            InboundApiKeyHash = "inbound",
            SharedKey = sharedKey,
            IsServer = false,
            Status = Werkr.Common.Models.ConnectionStatus.Connected,
        };
    }
}
