using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;
using System.Threading.Channels;
using Werkr.Common.Rendering;
using Werkr.Core.Communication;

namespace Werkr.Agent.Operators;

/// <summary>
/// Routes PowerShell host UI output (<c>Write-Host</c>, <c>Format-Table</c>, etc.)
/// into the operator's <see cref="ChannelWriter{T}"/> for streaming.
/// This is the single output path for all PowerShell output - stream
/// <c>DataAdded</c> handlers are not used.
/// </summary>
/// <remarks>
/// Non-interactive: all input/prompt methods throw <see cref="NotSupportedException"/>.
/// </remarks>
/// <remarks>Creates a new <see cref="WerkrPSHostUserInterface"/>.</remarks>
/// <param name="writer">Channel to write operator output into.</param>
/// <param name="bufferWidth">Column width for the raw UI buffer.</param>
public sealed class WerkrPSHostUserInterface( ChannelWriter<OperatorOutput> writer, int bufferWidth ) : PSHostUserInterface {
    /// <summary>
    /// The channel writer to which all PowerShell output is written as <see cref="OperatorOutput"/> messages.
    /// </summary>
    private readonly ChannelWriter<OperatorOutput> _writer = writer;
    /// <summary>
    /// The raw user interface providing buffer dimensions and virtual console properties.
    /// </summary>
    private readonly WerkrPSHostRawUserInterface _rawUI = new( bufferWidth );

    /// <summary>
    /// The raw user interface providing buffer dimensions and virtual console properties.
    /// </summary>
    public override PSHostRawUserInterface RawUI => _rawUI;

    /// <inheritdoc/>
    public override void Write( string value )
        => _writer.TryWrite( OperatorOutput.Create( "Information", value ) );

    /// <inheritdoc/>
    public override void Write(
        ConsoleColor foregroundColor,
        ConsoleColor backgroundColor,
        string value
    ) {
        string fgAnsi = AnsiHtmlConverter.ConsoleColorToAnsi( foregroundColor );
        string bgAnsi = AnsiHtmlConverter.ConsoleColorToBgAnsi( backgroundColor );
        string encoded = $"{fgAnsi}{bgAnsi}{value}{AnsiHtmlConverter.AnsiReset}";
        _ = _writer.TryWrite( OperatorOutput.Create( "Information", encoded ) );
    }

    /// <inheritdoc/>
    public override void WriteLine( string value )
        => _writer.TryWrite( OperatorOutput.Create( "Information", value ) );

    /// <inheritdoc/>
    public override void WriteDebugLine( string message )
        => _writer.TryWrite( OperatorOutput.Create( "Debug", message ) );

    /// <inheritdoc/>
    public override void WriteVerboseLine( string message )
        => _writer.TryWrite( OperatorOutput.Create( "Verbose", message ) );

    /// <inheritdoc/>
    public override void WriteWarningLine( string message )
        => _writer.TryWrite( OperatorOutput.Create( "Warning", message ) );

    /// <inheritdoc/>
    public override void WriteErrorLine( string message )
        => _writer.TryWrite( OperatorOutput.Create( "Error", message ) );

    /// <inheritdoc/>
    public override void WriteProgress( long sourceId, ProgressRecord record )
        => _writer.TryWrite( OperatorOutput.Create( "Progress",
            $"{record.Activity}: {record.StatusDescription} ({record.PercentComplete}%)" ) );

    /// <inheritdoc/>
    public override string ReadLine( )
        => throw new NotSupportedException( "Non-interactive host." );

    /// <inheritdoc/>
    public override SecureString ReadLineAsSecureString( )
        => throw new NotSupportedException( "Non-interactive host." );

    /// <inheritdoc/>
    public override Dictionary<string, PSObject> Prompt(
        string caption, string message, Collection<FieldDescription> descriptions )
        => throw new NotSupportedException( "Non-interactive host." );

    /// <inheritdoc/>
    public override PSCredential PromptForCredential(
        string caption, string message, string userName, string targetName )
        => throw new NotSupportedException( "Non-interactive host." );

    /// <inheritdoc/>
    public override PSCredential PromptForCredential(
        string caption, string message, string userName, string targetName,
        PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options )
        => throw new NotSupportedException( "Non-interactive host." );

    /// <inheritdoc/>
    public override int PromptForChoice(
        string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice )
        => throw new NotSupportedException( "Non-interactive host." );
}
