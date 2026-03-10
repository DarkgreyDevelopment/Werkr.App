namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Minimal <see cref="ServerCallContext"/> stub for unit-testing gRPC interceptors.
/// </summary>
internal sealed class TestServerCallContext : ServerCallContext {
    /// <summary>
    /// The request metadata headers supplied during construction.
    /// </summary>
    private readonly Metadata _requestHeaders;
    /// <summary>
    /// The cancellation token supplied during construction.
    /// </summary>
    private readonly CancellationToken _cancellationToken;
    /// <summary>
    /// Mutable user-state dictionary exposed to both the production code
    /// (via the base class) and to tests
    /// (via <see cref="ExposedUserState"/>).
    /// </summary>
    private readonly Dictionary<object, object> _userState = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="TestServerCallContext"/> class.
    /// </summary>
    private TestServerCallContext(
        Metadata requestHeaders,
        CancellationToken cancellationToken
    ) {
        _requestHeaders = requestHeaders;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Creates a new <see cref="TestServerCallContext"/> with optional request headers and cancellation token.
    /// </summary>
    public static TestServerCallContext Create(
        Metadata? requestHeaders = null,
        CancellationToken cancellationToken = default ) {
        return new TestServerCallContext(
            requestHeaders ?? [],
            cancellationToken
        );
    }

    /// <inheritdoc />
    protected override string MethodCore => "/test/Method";
    /// <inheritdoc />
    protected override string HostCore => "localhost";
    /// <inheritdoc />
    protected override string PeerCore => "ipv4:127.0.0.1:12345";
    /// <inheritdoc />
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    /// <inheritdoc />
    protected override Metadata RequestHeadersCore => _requestHeaders;
    /// <inheritdoc />
    protected override CancellationToken CancellationTokenCore => _cancellationToken;
    /// <inheritdoc />
    protected override Metadata ResponseTrailersCore => [];
    /// <inheritdoc />
    protected override Status StatusCore { get; set; }
    /// <inheritdoc />
    protected override WriteOptions? WriteOptionsCore { get; set; }
    /// <inheritdoc />
    protected override AuthContext AuthContextCore => new( string.Empty, [] );

    /// <summary>Exposes the user-state dictionary that interceptors write to.</summary>
    public IDictionary<object, object> ExposedUserState => _userState;

    /// <inheritdoc />
    protected override IDictionary<object, object> UserStateCore => _userState;

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">
    /// Always thrown; propagation tokens are not supported in tests.
    /// </exception>
    protected override ContextPropagationToken CreatePropagationTokenCore(
        ContextPropagationOptions? options
    ) {
        throw new NotImplementedException( );
    }

    /// <inheritdoc />
    protected override Task WriteResponseHeadersAsyncCore( Metadata responseHeaders ) {
        return Task.CompletedTask;
    }
}
