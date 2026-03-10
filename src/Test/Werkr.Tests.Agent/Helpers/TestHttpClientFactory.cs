namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// A test <see cref="IHttpClientFactory"/> that returns an <see cref="HttpClient"/>
/// backed by a <see cref="MockHttpMessageHandler"/>.
/// </summary>
internal sealed class TestHttpClientFactory : IHttpClientFactory {

    private readonly MockHttpMessageHandler _handler;

    /// <summary>Creates a factory backed by the specified mock handler.</summary>
    public TestHttpClientFactory( MockHttpMessageHandler handler ) {
        _handler = handler;
    }

    /// <inheritdoc/>
    public HttpClient CreateClient( string name ) => new( _handler );
}
