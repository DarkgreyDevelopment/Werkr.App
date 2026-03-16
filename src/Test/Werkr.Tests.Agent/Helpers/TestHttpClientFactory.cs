namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// A test <see cref="IHttpClientFactory"/> that returns an <see cref="HttpClient"/>
/// backed by a <see cref="MockHttpMessageHandler"/>.
/// </summary>
/// <remarks>Creates a factory backed by the specified mock handler.</remarks>
internal sealed class TestHttpClientFactory( MockHttpMessageHandler handler ) : IHttpClientFactory {

    private readonly MockHttpMessageHandler _handler = handler;

    /// <inheritdoc/>
    public HttpClient CreateClient( string name ) => new( _handler );
}
