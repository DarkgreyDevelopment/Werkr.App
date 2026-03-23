namespace Werkr.Data.Entities.Audit;

/// <summary>
/// Identifies the type of actor that performed an audited action.
/// </summary>
public enum ActorType {
    /// <summary>A human user authenticated via Identity.</summary>
    User = 0,

    /// <summary>An automated system process (e.g., background service, scheduler).</summary>
    System = 1,

    /// <summary>A programmatic caller authenticated via API key.</summary>
    ApiKey = 2,

    /// <summary>A registered Werkr Agent.</summary>
    Agent = 3
}
