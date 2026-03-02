using Grpc.Core;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Minimal <see cref="ServerCallContext"/> stub for unit-testing gRPC interceptors.
/// </summary>
internal sealed class TestServerCallContext : ServerCallContext {
    private readonly Metadata _requestHeaders;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<object, object> _userState = [];

    private TestServerCallContext( Metadata requestHeaders, CancellationToken cancellationToken ) {
        _requestHeaders = requestHeaders;
        _cancellationToken = cancellationToken;
    }

    public static TestServerCallContext Create(
        Metadata? requestHeaders = null,
        CancellationToken cancellationToken = default ) {
        return new TestServerCallContext( requestHeaders ?? [], cancellationToken );
    }

    protected override string MethodCore => "/test/Method";
    protected override string HostCore => "localhost";
    protected override string PeerCore => "ipv4:127.0.0.1:12345";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore => _requestHeaders;
    protected override CancellationToken CancellationTokenCore => _cancellationToken;
    protected override Metadata ResponseTrailersCore => [];
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new( string.Empty, [] );

    /// <summary>Exposes the user-state dictionary that interceptors write to.</summary>
    public IDictionary<object, object> ExposedUserState => _userState;

    protected override IDictionary<object, object> UserStateCore => _userState;

    protected override ContextPropagationToken CreatePropagationTokenCore( ContextPropagationOptions? options ) {
        throw new NotImplementedException( );
    }

    protected override Task WriteResponseHeadersAsyncCore( Metadata responseHeaders ) {
        return Task.CompletedTask;
    }
}
