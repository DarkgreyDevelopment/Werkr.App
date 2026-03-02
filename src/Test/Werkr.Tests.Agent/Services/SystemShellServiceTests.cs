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

[TestClass]
public class SystemShellServiceTests {
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RunCommand_ValidEncryptedRequest_WritesEncryptedOutput( ) {
        byte[] sharedKey = EncryptionProvider.GenerateRandomBytes( 32 );
        RegisteredConnection connection = CreateConnection( sharedKey );
        string keyId = connection.Id.ToString( );

        SystemShellService service = new(
            new SystemShellOperator( NullLogger<SystemShellOperator>.Instance ),
            Options.Create( new AgentSettings { EnableSystemShell = true } ),
            NullLogger<SystemShellService>.Instance );

        ShellRequest innerRequest = new( ) { Command = "echo shell-service-test" };
        EncryptedEnvelope request = PayloadEncryptor.EncryptToEnvelope( innerRequest, sharedKey, keyId );

        MockServerStreamWriter<EncryptedEnvelope> stream = new( );
        TestServerCallContext context = TestServerCallContext.Create( cancellationToken: TestContext.CancellationToken );
        context.ExposedUserState["Connection"] = connection;

        await service.RunCommand( request, stream, context );

        List<string> decryptedMessages = [.. stream.Messages.Select( envelope => {
            GrpcLogMsg logMsg = PayloadEncryptor.DecryptFromEnvelope<GrpcLogMsg>( envelope, sharedKey );
            return logMsg.Message;
        } )];

        Assert.AreNotEqual( -1, decryptedMessages.FindIndex( m => m.Contains( "shell-service-test", StringComparison.OrdinalIgnoreCase ) ) );
    }

    [TestMethod]
    public async Task RunCommand_WhenSystemShellDisabled_ThrowsUnimplemented( ) {
        byte[] sharedKey = EncryptionProvider.GenerateRandomBytes( 32 );
        RegisteredConnection connection = CreateConnection( sharedKey );
        string keyId = connection.Id.ToString( );

        SystemShellService service = new(
            new SystemShellOperator( NullLogger<SystemShellOperator>.Instance ),
            Options.Create( new AgentSettings { EnableSystemShell = false } ),
            NullLogger<SystemShellService>.Instance );

        ShellRequest innerRequest = new( ) { Command = "echo x" };
        EncryptedEnvelope request = PayloadEncryptor.EncryptToEnvelope( innerRequest, sharedKey, keyId );

        MockServerStreamWriter<EncryptedEnvelope> stream = new( );
        TestServerCallContext context = TestServerCallContext.Create( cancellationToken: TestContext.CancellationToken );
        context.ExposedUserState["Connection"] = connection;

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await service.RunCommand( request, stream, context ) );

        Assert.AreEqual( StatusCode.Unimplemented, ex.StatusCode );
    }

    [TestMethod]
    public async Task RunCommand_MissingConnection_ThrowsInternal( ) {
        byte[] sharedKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string keyId = Guid.NewGuid( ).ToString( );

        SystemShellService service = new(
            new SystemShellOperator( NullLogger<SystemShellOperator>.Instance ),
            Options.Create( new AgentSettings { EnableSystemShell = true } ),
            NullLogger<SystemShellService>.Instance );

        ShellRequest innerRequest = new( ) { Command = "echo x" };
        EncryptedEnvelope request = PayloadEncryptor.EncryptToEnvelope( innerRequest, sharedKey, keyId );

        MockServerStreamWriter<EncryptedEnvelope> stream = new( );
        TestServerCallContext context = TestServerCallContext.Create( cancellationToken: TestContext.CancellationToken );

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await service.RunCommand( request, stream, context ) );

        Assert.AreEqual( StatusCode.Internal, ex.StatusCode );
    }

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
