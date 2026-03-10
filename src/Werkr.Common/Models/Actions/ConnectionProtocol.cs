namespace Werkr.Common.Models.Actions;

/// <summary>
/// The connection protocol used by the <c>TestConnection</c> action.
/// </summary>
public enum ConnectionProtocol {

    /// <summary>Raw TCP socket connection.</summary>
    Tcp,

    /// <summary>HTTP connection (sends HEAD request).</summary>
    Http,

    /// <summary>HTTPS connection (sends HEAD request over TLS).</summary>
    Https,
}
