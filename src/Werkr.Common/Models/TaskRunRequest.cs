namespace Werkr.Common.Models;

/// <summary>Request DTO for triggering an ad-hoc task run.</summary>
public sealed record TaskRunRequest( Guid? AgentConnectionId = null );
