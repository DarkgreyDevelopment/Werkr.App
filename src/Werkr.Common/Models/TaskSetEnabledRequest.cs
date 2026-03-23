namespace Werkr.Common.Models;

/// <summary>Request DTO for toggling task enabled state.</summary>
public sealed record TaskSetEnabledRequest( bool Enabled );
