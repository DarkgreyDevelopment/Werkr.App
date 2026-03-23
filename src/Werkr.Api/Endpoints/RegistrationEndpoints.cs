using System.Security.Claims;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Registration;

namespace Werkr.Api.Endpoints;

/// <summary>Maps the registration bundle generation endpoint.</summary>
internal static class RegistrationEndpoints {
    /// <summary>Maps <c>POST /api/registration/generate</c>.</summary>
    public static WebApplication MapRegistrationEndpoints( this WebApplication app ) {
        _ = app.MapPost(
            "/api/v1/registration/generate",
            async (
                RegistrationGenerateRequest request,
                ClaimsPrincipal user,
                RegistrationService registrationService,
                IAuditService auditService,
                CancellationToken ct
            ) => {
                int? expirationMinutes = request.ExpirationMinutes;
                TimeSpan? expiration = expirationMinutes switch {
                    null => null,
                    <= 0 => TimeSpan.Zero,
                    _ => TimeSpan.FromMinutes( expirationMinutes.Value )
                };

                string bundle = await registrationService.GenerateBundleAsync(
                    request.ConnectionName, request.Password, expiration, request.Tags, ct );

                // Audit: agent registration bundle generated
                string? userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
                await auditService.LogAsync( new AuditEntry(
                    EventTypeId: AuditEventType.AgentRegistered.ToEventId( ),
                    ActorId: userId,
                    ActorType: "User",
                    EntityType: "Agent",
                    EntityId: null,
                    ActionPerformed: "BundleGenerated",
                    Details: new { AgentName = request.ConnectionName }
                ), ct );

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
