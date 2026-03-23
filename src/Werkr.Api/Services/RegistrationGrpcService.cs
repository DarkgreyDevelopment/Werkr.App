using Grpc.Core;
using Werkr.Common.Models.Audit;
using Werkr.Common.Protos;
using Werkr.Core.Audit;
using Werkr.Core.Communication;
using Werkr.Core.Cryptography;
using Werkr.Core.Registration;
using Werkr.Core.Registration.Models;

namespace Werkr.Api.Services;

/// <summary>
/// gRPC service endpoint for Agent registration.
/// Decrypts the incoming <see cref="EncryptedEnvelope"/> using the password-derived
/// registration key, delegates to <see cref="RegistrationService"/>, and encrypts the
/// response back into an <see cref="EncryptedEnvelope"/>.
/// </summary>
/// <remarks>
/// Creates a new <see cref="RegistrationGrpcService"/>.
/// </remarks>
/// <param name="registrationService">The core registration service.</param>
/// <param name="auditService">Audit event service.</param>
/// <param name="logger">Logger for diagnostics.</param>
public partial class RegistrationGrpcService(
    RegistrationService registrationService,
    IAuditService auditService,
    ILogger<RegistrationGrpcService> logger
) : AgentRegistration.AgentRegistrationBase {

    /// <summary>
    /// Handles the Agent's RegisterAgent gRPC call.
    /// The request and response are wrapped in <see cref="EncryptedEnvelope"/> using the
    /// password-derived AES-256 key stored on the <c>RegistrationBundle</c>.
    /// </summary>
    /// <param name="request">The encrypted envelope containing a <see cref="RegisterAgentRequest"/>.</param>
    /// <param name="context">The gRPC server call context.</param>
    /// <returns>An encrypted envelope containing a <see cref="RegisterAgentResponse"/>.</returns>
    public override async Task<EncryptedEnvelope> RegisterAgent(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {
        try {
            // Extract the registration key placed by AgentBearerTokenInterceptor
            if (!context.UserState.TryGetValue( "RegistrationKey", out object? keyObj )
                || keyObj is not byte[] registrationKey) {
                throw new RpcException( new Status( StatusCode.Unauthenticated,
                    "Registration key not resolved by interceptor." ) );
            }

            // Decrypt the request envelope
            RegisterAgentRequest innerRequest = PayloadEncryptor.DecryptFromEnvelope<RegisterAgentRequest>(
                request, registrationKey );

            (AgentRegistrationResult result, byte[]? encryptedResponseData) = await registrationService.CompleteRegistrationAsync(
                innerRequest.BundleId.ToByteArray( ),
                innerRequest.EncryptedAgentPublicKey.ToByteArray( ),
                innerRequest.AgentUrl,
                innerRequest.AgentName,
                innerRequest.AgentVersion,
                context.CancellationToken );

            if (result.Success) {
                await auditService.LogAsync( new AuditEntry(
                    EventTypeId: AuditEventType.AgentRegistrationCompleted.ToEventId( ),
                    ActorId: null,
                    ActorType: "System",
                    EntityType: "Agent",
                    EntityId: null,
                    ActionPerformed: "RegistrationCompleted",
                    Details: new { AgentName = innerRequest.AgentName, AgentUrl = innerRequest.AgentUrl }
                ), context.CancellationToken );
            }

            RegisterAgentResponse response = new( ) {
                Success = result.Success,
                EncryptedRegistrationData = encryptedResponseData is not null
                    ? Google.Protobuf.ByteString.CopyFrom( encryptedResponseData )
                    : Google.Protobuf.ByteString.Empty,
                Message = result.ErrorMessage ?? string.Empty,
            };

            // Encrypt the response back into an EncryptedEnvelope
            return SecureResponseBuilder.EncryptResponse( response, registrationKey, "registration" );
        } catch (WerkrCryptoException ex) {
            logger.LogError( ex, "Cryptographic error during Agent registration." );
            throw new RpcException( new Status( StatusCode.InvalidArgument, ex.Message ) );
        } catch (RpcException) {
            throw;
        } catch (Exception ex) {
            logger.LogError( ex, "Unexpected error during Agent registration." );
            throw new RpcException( new Status( StatusCode.Internal, "Internal registration error." ) );
        }
    }
}
