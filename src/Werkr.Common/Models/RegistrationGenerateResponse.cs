namespace Werkr.Common.Models;

/// <summary>Response body for POST /api/registration/generate.</summary>
/// <param name="Success">True when the bundle was generated successfully.</param>
/// <param name="EncryptedBundle">The encrypted bundle payload.</param>
/// <param name="Message">Optional informational or error message.</param>
public sealed record RegistrationGenerateResponse(
    bool Success,
    string? EncryptedBundle,
    string? Message
);
