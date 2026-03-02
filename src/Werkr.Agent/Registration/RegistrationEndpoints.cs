using System.Text.Json;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

using Werkr.Core.Registration.Models;
using Werkr.Data;

namespace Werkr.Agent.Registration;

/// <summary>
/// Maps the localhost-only registration endpoints for the Agent.
/// </summary>
public static class RegistrationEndpoints {
    /// <summary>
    /// Maps the <c>GET /register</c> and <c>POST /register</c> endpoints,
    /// restricted to localhost connections only.
    /// </summary>
    /// <param name="app">The web application to map endpoints on.</param>
    /// <returns>The <see cref="RouteGroupBuilder"/> for further chaining.</returns>
    public static RouteGroupBuilder MapRegistrationEndpoints( this WebApplication app ) {
        RouteGroupBuilder group = app.MapGroup( "/register" )
            .RequireHost( "localhost", "127.0.0.1" );

        _ = group.MapGet( "/", HandleGetRegistrationPage );
        _ = group.MapPost( "/", HandlePostRegistration );

        return group;
    }

    /// <summary>
    /// Returns a simple HTML registration page.
    /// </summary>
    private static IResult HandleGetRegistrationPage( IConfiguration configuration, IServer server ) {
        string agentUrl = ResolveAgentUrl( configuration, server );

        string html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Werkr Agent Registration</title>
    <style>
        *, *::before, *::after { box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
               max-width: 640px; margin: 2rem auto; padding: 0 1rem; color: #1a1a1a;
               background: #f8f9fa; }
        h1 { font-size: 1.4rem; margin-bottom: 0.25rem; }
        p.subtitle { color: #555; margin-top: 0; }
        label { display: block; font-weight: 600; margin-top: 1rem; margin-bottom: 0.25rem; }
        textarea, input[type="text"], input[type="password"] {
            width: 100%; padding: 0.5rem; border: 1px solid #ccc; border-radius: 4px;
            font-family: inherit; font-size: 0.95rem; }
        textarea { height: 120px; resize: vertical; }
        button { margin-top: 1.25rem; padding: 0.6rem 1.5rem; font-size: 1rem;
                 background: #0d6efd; color: #fff; border: none; border-radius: 4px;
                 cursor: pointer; }
        button:hover { background: #0b5ed7; }
        button:disabled { background: #6c757d; cursor: not-allowed; }
        #result { margin-top: 1.25rem; padding: 0.75rem; border-radius: 4px; display: none; }
        .success { background: #d1e7dd; border: 1px solid #badbcc; color: #0f5132; }
        .error { background: #f8d7da; border: 1px solid #f5c2c7; color: #842029; }
    </style>
</head>
<body>
    <h1>Werkr Agent Registration</h1>
    <p class="subtitle">Paste the registration bundle from your Server admin and enter the password.</p>

    <form id="regForm">
        <label for="bundle">Registration Bundle</label>
        <textarea id="bundle" name="bundle" placeholder="Paste encrypted bundle here..." required></textarea>

        <label for="password">Password</label>
        <input type="password" id="password" name="password" required />

        <div style="margin-top: 1rem;">
            <span style="font-weight: 600;">Agent URL</span>
            <div style="font-family: monospace; color: #555; margin-top: 0.25rem;">{{agentUrl}}</div>
            <div style="font-size: 0.85rem; color: #6c757d; margin-top: 0.15rem;">This Agent's gRPC endpoint URL (from configuration).</div>
        </div>

        <button type="submit" id="submitBtn">Register</button>
    </form>

    <div id="result"></div>

    <script>
        document.getElementById('regForm').addEventListener('submit', async function(e) {
            e.preventDefault();
            const btn = document.getElementById('submitBtn');
            const result = document.getElementById('result');
            btn.disabled = true;
            btn.textContent = 'Registering...';
            result.style.display = 'none';

            try {
                const resp = await fetch('/register', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        bundle: document.getElementById('bundle').value,
                        password: document.getElementById('password').value
                    })
                });
                const data = await resp.json();
                result.style.display = 'block';
                result.className = data.success ? 'success' : 'error';
                result.textContent = data.message;
            } catch (err) {
                result.style.display = 'block';
                result.className = 'error';
                result.textContent = 'Request failed: ' + err.message;
            } finally {
                btn.disabled = false;
                btn.textContent = 'Register';
            }
        });
    </script>
</body>
</html>
""";
        return Results.Content( html, "text/html" );
    }

    /// <summary>
    /// Processes a registration bundle submitted by the admin.
    /// </summary>
    private static async Task<IResult> HandlePostRegistration(
        HttpRequest request,
        IConfiguration configuration,
        IServer server,
        AgentRegistrationHandler handler,
        WerkrDbContext dbContext,
        CancellationToken ct ) {

        RegistrationRequest? body;
        try {
            body = await request.ReadFromJsonAsync<RegistrationRequest>( ct );
        } catch (JsonException) {
            return Results.BadRequest( new RegistrationResponse( false, "Invalid JSON request body." ) );
        }

        if (body is null
             || string.IsNullOrWhiteSpace( body.Bundle )
             || string.IsNullOrWhiteSpace( body.Password )) {
            return Results.BadRequest( new RegistrationResponse( false,
                "Missing required fields: bundle, password." ) );
        }

        string agentUrl = ResolveAgentUrl( configuration, server );

        AgentRegistrationResult result = await handler.ProcessBundleAsync(
            body.Bundle, body.Password, agentUrl, dbContext, ct );

        return Results.Json( new RegistrationResponse( result.Success, result.ErrorMessage ?? string.Empty ) );
    }

    /// <summary>
    /// Resolves the agent's externally-reachable gRPC URL.
    /// Checks <c>Werkr:AgentUrl</c> configuration first; if not set, discovers
    /// the bound address from <see cref="IServer"/> (dynamic Aspire ports).
    /// </summary>
    private static string ResolveAgentUrl( IConfiguration configuration, IServer server ) {
        string? configured = configuration.GetValue<string>( "Werkr:AgentUrl" );
        if (!string.IsNullOrWhiteSpace( configured )) {
            return configured;
        }

        IServerAddressesFeature? addresses = server.Features.Get<IServerAddressesFeature>( );
        return addresses?.Addresses
            .FirstOrDefault( a => a.StartsWith( "https://", StringComparison.OrdinalIgnoreCase ) )
            ?? addresses?.Addresses.FirstOrDefault( )
            ?? "https://localhost:5001";
    }

    /// <summary>JSON request body for <c>POST /register</c>.</summary>
    /// <param name="Bundle">The encrypted registration bundle string.</param>
    /// <param name="Password">The password used to encrypt the bundle.</param>
    internal sealed record RegistrationRequest( string Bundle, string Password );

    /// <summary>JSON response body for <c>POST /register</c>.</summary>
    /// <param name="Success">Whether the registration succeeded.</param>
    /// <param name="Message">A user-facing message describing the outcome.</param>
    internal sealed record RegistrationResponse( bool Success, string Message );
}
