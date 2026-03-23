using System.Text.Json;
using Werkr.Server.Identity;

namespace Werkr.Server.Services;

/// <summary>
/// Scoped HTTP wrapper injected into Blazor components. Automatically resolves
/// the current user's JWT via <see cref="IUserTokenProvider"/> and flows it
/// through <see cref="UserTokenContext"/> so the pooled
/// <see cref="AuthForwardingHandler"/> attaches the correct identity.
/// </summary>
public sealed class ApiServiceAccessor(
    IHttpClientFactory httpClientFactory,
    IUserTokenProvider userTokenProvider
) {
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Sends a GET request and deserializes the JSON response.</summary>
    public async Task<T?> GetAsync<T>( string requestUri, CancellationToken ct = default ) {
        await SetUserTokenAsync( );
        HttpClient client = httpClientFactory.CreateClient( "ApiService" );
        return await client.GetFromJsonAsync<T>( requestUri, s_jsonOptions, ct );
    }

    /// <summary>Sends a GET request and returns the raw <see cref="HttpResponseMessage"/>.</summary>
    public async Task<HttpResponseMessage> GetRawAsync( string requestUri, CancellationToken ct = default ) {
        await SetUserTokenAsync( );
        HttpClient client = httpClientFactory.CreateClient( "ApiService" );
        return await client.GetAsync( requestUri, ct );
    }

    /// <summary>Sends a POST request with a JSON body.</summary>
    public async Task<HttpResponseMessage> PostAsJsonAsync<T>( string requestUri, T value, CancellationToken ct = default ) {
        await SetUserTokenAsync( );
        HttpClient client = httpClientFactory.CreateClient( "ApiService" );
        return await client.PostAsJsonAsync( requestUri, value, s_jsonOptions, ct );
    }

    /// <summary>Sends a POST request with no body.</summary>
    public async Task<HttpResponseMessage> PostAsync( string requestUri, HttpContent? content = null, CancellationToken ct = default ) {
        await SetUserTokenAsync( );
        HttpClient client = httpClientFactory.CreateClient( "ApiService" );
        return await client.PostAsync( requestUri, content, ct );
    }

    /// <summary>Sends a PUT request with a JSON body.</summary>
    public async Task<HttpResponseMessage> PutAsJsonAsync<T>( string requestUri, T value, CancellationToken ct = default ) {
        await SetUserTokenAsync( );
        HttpClient client = httpClientFactory.CreateClient( "ApiService" );
        return await client.PutAsJsonAsync( requestUri, value, s_jsonOptions, ct );
    }

    /// <summary>Sends a DELETE request.</summary>
    public async Task<HttpResponseMessage> DeleteAsync( string requestUri, CancellationToken ct = default ) {
        await SetUserTokenAsync( );
        HttpClient client = httpClientFactory.CreateClient( "ApiService" );
        return await client.DeleteAsync( requestUri, ct );
    }

    /// <summary>Sends an arbitrary <see cref="HttpRequestMessage"/>.</summary>
    public async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken ct = default ) {
        await SetUserTokenAsync( );
        HttpClient client = httpClientFactory.CreateClient( "ApiService" );
        return await client.SendAsync( request, ct );
    }

    /// <summary>Sends an arbitrary <see cref="HttpRequestMessage"/> with the specified completion option.</summary>
    public async Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken ct = default ) {
        await SetUserTokenAsync( );
        HttpClient client = httpClientFactory.CreateClient( "ApiService" );
        return await client.SendAsync( request, completionOption, ct );
    }

    private async Task SetUserTokenAsync( ) {
        string? token = await userTokenProvider.GetTokenAsync( );
        UserTokenContext.CurrentToken = token;
    }
}
