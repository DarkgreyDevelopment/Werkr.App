using System.Globalization;
using System.Management.Automation.Host;
using System.Threading.Channels;
using Werkr.Core.Communication;

namespace Werkr.Agent.Operators;

/// <summary>
/// Minimal <see cref="PSHost"/> implementation for headless PowerShell SDK hosting.
/// Provides a <see cref="WerkrPSHostUserInterface"/> that routes all output
/// (<c>Write-Host</c>, <c>Format-Table</c>, <c>Write-Error</c>, etc.) into a
/// <see cref="ChannelWriter{T}"/> for streaming to the server and console UI.
/// </summary>
/// <remarks>
/// Non-interactive: <see cref="EnterNestedPrompt"/> and <see cref="ExitNestedPrompt"/>
/// throw <see cref="NotSupportedException"/>.
/// </remarks>
/// <remarks>Creates a new <see cref="WerkrPSHost"/>.</remarks>
/// <param name="writer">Channel to write operator output into.</param>
/// <param name="bufferWidth">Column width for the formatting subsystem. Default 150.</param>
public sealed class WerkrPSHost( ChannelWriter<OperatorOutput> writer, int bufferWidth = 150 ) : PSHost {
    /// <summary>
    /// Unique identifier for this host instance, generated at construction time.
    /// </summary>
    private readonly Guid _instanceId = Guid.NewGuid( );
    /// <summary>
    /// The user interface implementation that routes PowerShell output to the operator output channel.
    /// </summary>
    private readonly WerkrPSHostUserInterface _ui = new( writer, bufferWidth );

    /// <inheritdoc/>
    public override string Name => "WerkrPSHost";

    /// <inheritdoc/>
    public override Version Version => new( 1, 0, 0 );

    /// <inheritdoc/>
    public override Guid InstanceId => _instanceId;

    /// <inheritdoc/>
    public override CultureInfo CurrentCulture => CultureInfo.CurrentCulture;

    /// <inheritdoc/>
    public override CultureInfo CurrentUICulture => CultureInfo.CurrentUICulture;

    /// <inheritdoc/>
    public override PSHostUserInterface UI => _ui;

    /// <inheritdoc/>
    public override void SetShouldExit( int exitCode ) { /* no-op */ }

    /// <inheritdoc/>
    public override void EnterNestedPrompt( )
        => throw new NotSupportedException( "Non-interactive host." );

    /// <inheritdoc/>
    public override void ExitNestedPrompt( )
        => throw new NotSupportedException( "Non-interactive host." );

    /// <inheritdoc/>
    public override void NotifyBeginApplication( ) { /* no-op */ }

    /// <inheritdoc/>
    public override void NotifyEndApplication( ) { /* no-op */ }
}
