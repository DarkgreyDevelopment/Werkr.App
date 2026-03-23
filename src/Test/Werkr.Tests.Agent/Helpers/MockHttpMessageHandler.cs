using System.Net;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that returns a configurable response.
/// Used by network handler tests to avoid real HTTP traffic.
/// </summary>
/// <remarks>
/// Creates a handler that invokes the provided delegate for every request.
/// </remarks>
internal sealed class MockHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler
    ) : HttpMessageHandler {

    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler = handler;

    /// <summary>
    /// Creates a handler that always returns the specified response.
    /// </summary>
    public MockHttpMessageHandler( HttpResponseMessage response )
        : this( ( _, _ ) => Task.FromResult( response ) ) { }

    /// <summary>
    /// Creates a handler that returns 200 OK with the specified string body.
    /// </summary>
    public static MockHttpMessageHandler Ok( string body = "" ) =>
        new( new HttpResponseMessage( HttpStatusCode.OK ) {
            Content = new StringContent( body ),
        } );

    /// <summary>
    /// Creates a handler that returns the specified status code with the specified body.
    /// </summary>
    public static MockHttpMessageHandler WithStatus(
        HttpStatusCode statusCode,
        string body = ""
    ) =>
        new( new HttpResponseMessage( statusCode ) {
            Content = new StringContent( body ),
        } );

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) =>
        _handler( request, cancellationToken );
}
