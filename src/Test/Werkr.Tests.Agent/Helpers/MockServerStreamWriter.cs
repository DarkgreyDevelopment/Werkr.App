using Grpc.Core;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Mock <see cref="IServerStreamWriter{T}"/> that collects written messages for assertion.
/// </summary>
internal sealed class MockServerStreamWriter<T> : IServerStreamWriter<T> {
    private readonly List<T> _messages = [];

    /// <summary>Gets the messages written to this stream.</summary>
    public IReadOnlyList<T> Messages => _messages;

    /// <inheritdoc/>
    public WriteOptions? WriteOptions { get; set; }

    /// <inheritdoc/>
    public Task WriteAsync( T message ) {
        _messages.Add( message );
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task WriteAsync( T message, CancellationToken cancellationToken ) {
        cancellationToken.ThrowIfCancellationRequested( );
        _messages.Add( message );
        return Task.CompletedTask;
    }
}
