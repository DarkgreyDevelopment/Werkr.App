namespace Werkr.Common.Models;

/// <summary>Request body for exchanging an API key for a JWT bearer token.</summary>
public sealed record TokenRequest( string ApiKey );
