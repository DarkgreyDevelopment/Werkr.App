using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Core.Registration;

namespace Werkr.Api.Endpoints;

/// <summary>Maps the registration bundle generation endpoint.</summary>
internal static class RegistrationEndpoints {
    /// <summary>Maps <c>POST /api/registration/generate</c>.</summary>
    public static WebApplication MapRegistrationEndpoints( this WebApplication app ) {
        _ = app.MapPost( "/api/registration/generate", async (
            RegistrationGenerateRequest request,
            RegistrationService registrationService,
            CancellationToken ct ) => {
                int? expirationMinutes = request.ExpirationMinutes;
                TimeSpan? expiration = expirationMinutes switch {
                    null => null,
                    <= 0 => TimeSpan.Zero,
                    _ => TimeSpan.FromMinutes( expirationMinutes.Value )
                };

                string bundle = await registrationService.GenerateBundleAsync(
                    request.ConnectionName, request.Password, expiration, request.Tags, ct );

                RegistrationGenerateResponse response = new(
                    Success: true,
                    EncryptedBundle: bundle,
                    Message: "Bundle generated." );

                return Results.Ok( response );
            } )
        .WithName( "GenerateRegistrationBundle" )
        .RequireAuthorization( Policies.IsAdmin );

        return app;
    }
}
