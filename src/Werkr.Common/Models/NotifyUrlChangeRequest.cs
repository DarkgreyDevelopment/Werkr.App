namespace Werkr.Common.Models;

/// <summary>Request body for notifying agents of a server URL change.</summary>
public sealed record NotifyUrlChangeRequest( string NewServerUrl );
