using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Registration;

namespace Werkr.Data.Entities.Configuration;

/// <summary>
/// Join entity restricting a <see cref="Credential"/> to specific agents.
/// When no scope entries exist for a credential, it is available to all agents.
/// When scope entries exist, only the listed agents may access it.
/// </summary>
[Table( "credential_agent_scopes" )]
public class CredentialAgentScope {

    /// <summary>Foreign key to the credential.</summary>
    public long CredentialId { get; set; }

    /// <summary>Foreign key to the agent connection.</summary>
    public Guid AgentConnectionId { get; set; }

    /// <summary>Navigation to the credential.</summary>
    [ForeignKey( nameof( CredentialId ) )]
    public Credential? Credential { get; set; }

    /// <summary>Navigation to the agent connection.</summary>
    [ForeignKey( nameof( AgentConnectionId ) )]
    public RegisteredConnection? AgentConnection { get; set; }
}
