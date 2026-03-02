namespace Werkr.Core.Registration.Models;

/// <summary>
/// Data hybrid-encrypted in the Server's gRPC response during registration.
/// Contains bidirectional API keys and the pre-shared symmetric key.
/// Serialized to JSON, then hybrid-encrypted with the Agent's RSA public key.
/// </summary>
/// <param name="AgentToServerApiKey">The raw API key for the Agent to call the Server. Agent stores as <c>OutboundApiKey</c>.</param>
/// <param name="ServerToAgentApiKey">The raw API key for the Server to call the Agent. Agent stores hash as <c>InboundApiKeyHash</c>.</param>
/// <param name="SharedKey">The 32-byte AES-256 symmetric key for ongoing message encryption.</param>
/// <param name="ConnectionId">The shared connection ID both sides use for post-registration communication.</param>
public sealed record RegistrationResponsePayload(
    string AgentToServerApiKey,
    string ServerToAgentApiKey,
    byte[] SharedKey,
    Guid ConnectionId );
