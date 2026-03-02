namespace Werkr.Common.Auth;

/// <summary>
/// Shared JWT claim type constants used by both the Server (issuer) and API (validator).
/// </summary>
public static class WerkrClaimTypes {
    /// <summary>Claim type for permission values embedded in JWTs.</summary>
    public const string Permission = "permission";

    /// <summary>Claim type for the API key identifier.</summary>
    public const string ApiKeyId = "api_key_id";

    /// <summary>Claim type for the API key display name.</summary>
    public const string ApiKeyName = "api_key_name";
}
