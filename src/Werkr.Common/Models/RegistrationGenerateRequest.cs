namespace Werkr.Common.Models;

/// <summary>Request body for POST /api/registration/generate.</summary>
/// <param name="ConnectionName">Admin-assigned label for the Agent connection.</param>
/// <param name="Password">Password used to encrypt the registration bundle.</param>
/// <param name="ExpirationMinutes">Bundle expiration in minutes (null = default 24 hours, 0 = never).</param>
/// <param name="Tags">Optional tags to assign to the agent upon registration completion.</param>
public sealed record RegistrationGenerateRequest(
    string ConnectionName,
    string Password,
    int? ExpirationMinutes,
    string[]? Tags = null );
