namespace Werkr.Common.Models;

/// <summary>Request body for the retry-from-failed endpoint.</summary>
public sealed record RetryFromFailedRequest(
    Dictionary<string, string>? VariableOverrides
);
