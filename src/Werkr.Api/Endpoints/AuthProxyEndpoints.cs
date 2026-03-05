using Werkr.Common.Models;

namespace Werkr.Api.Endpoints;

/// <summary>
/// Maps the token proxy endpoint on the API. The API does NOT issue tokens -
/// it forwards <see cref="TokenRequest"/> to the Server (the sole JWT issuer)
/// and returns the <see cref="TokenResponse"/> verbatim (Decision A14).
/// </summary>
public static class AuthProxyEndpoints {
    /// <summary>
    /// Maps <c>POST /api/auth/token</c> as a transparent pass-through to the Server.
    /// </summary>
    public static WebApplication MapAuthProxyEndpoints( this WebApplication app ) {
        _ = app.MapPost(
            "/api/auth/token",
            async (
                TokenRequest request,
                IHttpClientFactory httpClientFactory,
                CancellationToken ct
            ) => {
                if (string.IsNullOrWhiteSpace( request.ApiKey )) {
                    return Results.BadRequest( new { message = "API key is required." } );
                }

                // Use the raw server client — no auth header (user is requesting a token)
                using HttpClient serverClient = httpClientFactory.CreateClient( "ServerService" );

                using HttpResponseMessage response = await serverClient.PostAsJsonAsync(
                    "/api/auth/token", request, ct );

                if (!response.IsSuccessStatusCode) {
                    // Forward the Server's status code (e.g. 401 for invalid key)
                    return Results.StatusCode( (int)response.StatusCode );
                }

                TokenResponse? tokenResponse = await response.Content
                    .ReadFromJsonAsync<TokenResponse>( ct );

                return tokenResponse is null
                    ? Results.Problem( "Token exchange returned an empty response." )
                    : Results.Ok( tokenResponse );
            } )
        .WithName( "ProxyTokenExchange" )
        .WithTags( "Auth" )
        .AllowAnonymous( );

        return app;
    }
}
