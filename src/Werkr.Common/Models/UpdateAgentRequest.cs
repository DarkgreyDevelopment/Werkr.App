namespace Werkr.Common.Models;

/// <summary>Request body for updating an agent connection.</summary>
/// <param name="ConnectionName">New display name (optional if RemoteUrl is provided).</param>
/// <param name="RemoteUrl">New gRPC endpoint URL for the agent (optional if ConnectionName is provided).</param>
public sealed record UpdateAgentRequest( string? ConnectionName, string? RemoteUrl );
