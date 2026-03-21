using Google.Protobuf;
using Grpc.Core;
using Werkr.Common.Models.Audit;
using Werkr.Common.Protos;
using Werkr.Core.Audit;
using Werkr.Core.Cryptography;
using Werkr.Core.Registration;
using Werkr.Core.Registration.Models;

namespace Werkr.Api.Services;

/// <summary>
/// gRPC service endpoint for Agent registration.
/// Pure pass-through to <see cref="RegistrationService"/> - zero business logic in API layer.
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
    /// </summary>
    /// <param name="request">The registration request from the Agent.</param>
    /// <param name="context">The gRPC server call context.</param>
    /// <returns>The registration response.</returns>
    public override async Task<RegisterAgentResponse> RegisterAgent(
        RegisterAgentRequest request,
        ServerCallContext context
    ) {
        try {
            (AgentRegistrationResult result, byte[]? encryptedResponseData) = await registrationService.CompleteRegistrationAsync(
                request.BundleId.ToByteArray( ),
                request.EncryptedAgentPublicKey.ToByteArray( ),
                request.AgentUrl,
                request.AgentName,
                request.AgentVersion,
                context.CancellationToken );

            if (result.Success) {
                await auditService.LogAsync( new AuditEntry(
                    EventTypeId: AuditEventType.AgentRegistrationCompleted.ToEventId( ),
                    ActorId: null,
                    ActorType: "System",
                    EntityType: "Agent",
                    EntityId: null,
                    ActionPerformed: "RegistrationCompleted",
                    Details: new { AgentName = request.AgentName, AgentUrl = request.AgentUrl }
                ), context.CancellationToken );
            }

            return new RegisterAgentResponse {
                Success = result.Success,
                EncryptedRegistrationData = encryptedResponseData is not null
                    ? ByteString.CopyFrom( encryptedResponseData )
                    : ByteString.Empty,
                Message = result.ErrorMessage ?? string.Empty,
            };
        } catch (WerkrCryptoException ex) {
            logger.LogError( ex, "Cryptographic error during Agent registration." );
            throw new RpcException( new Status( StatusCode.InvalidArgument, ex.Message ) );
        } catch (Exception ex) {
            logger.LogError( ex, "Unexpected error during Agent registration." );
            throw new RpcException( new Status( StatusCode.Internal, "Internal registration error." ) );
        }
    }
}
