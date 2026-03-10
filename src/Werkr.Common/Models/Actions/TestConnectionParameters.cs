namespace Werkr.Common.Models.Actions;

/// <summary>
/// Parameters for the <c>TestConnection</c> action — tests reachability of a
/// host/port via TCP, HTTP, or HTTPS. The action always succeeds; the
/// <c>reachable</c> result is data, not an error condition.
/// </summary>
public sealed record TestConnectionParameters {

    /// <summary>The hostname or IP address to connect to.</summary>
    public required string Host { get; init; }

    /// <summary>The port number to connect to.</summary>
    public required int Port { get; init; }

    /// <summary>Connection protocol. Default: <see cref="ConnectionProtocol.Tcp"/>.</summary>
    public ConnectionProtocol Protocol { get; init; } = ConnectionProtocol.Tcp;

    /// <summary>Connection timeout in seconds. Default: 10.</summary>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// For <see cref="ConnectionProtocol.Http"/>/<see cref="ConnectionProtocol.Https"/>:
    /// expected HTTP status code. When set and the actual status doesn't match,
    /// <c>reachable</c> is set to <see langword="false"/>.
    /// </summary>
    public int? ExpectedStatusCode { get; init; }
}
