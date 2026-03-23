using Werkr.Common.Protos;

namespace Werkr.Common.Communication;

/// <summary>
/// Implemented by all API-hosted gRPC response messages.
/// The proto-generated Metadata property satisfies this interface.
/// </summary>
public interface IHasResponseMetadata {
    /// <summary>Cross-cutting metadata piggybacked on every API response.</summary>
    ResponseMetadata? Metadata { get; set; }
}
