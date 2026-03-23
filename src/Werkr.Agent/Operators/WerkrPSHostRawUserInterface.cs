using System.Management.Automation.Host;

namespace Werkr.Agent.Operators;

/// <summary>
/// Provides buffer dimensions for PowerShell formatting cmdlets.
/// Sets <see cref="BufferSize"/> width to a configurable value (default 150) so that
/// <c>Format-Table</c> and <c>Format-List</c> produce correctly wrapped columnar output
/// instead of single-line or raw <c>FormatEntryData</c> type names.
/// </summary>
/// <remarks>
/// All setter properties are no-ops - the headless agent has no real console.
/// Input methods throw <see cref="NotSupportedException"/> (non-interactive).
/// </remarks>
/// <remarks>Creates a new <see cref="WerkrPSHostRawUserInterface"/> with the specified buffer width.</remarks>
/// <param name="bufferWidth">Column width for formatting cmdlets. Default 150.</param>
public sealed class WerkrPSHostRawUserInterface( int bufferWidth = 150 ) : PSHostRawUserInterface {
    /// <summary>
    /// The virtual buffer size reported to PowerShell for output formatting.
    /// </summary>
    private readonly Size _bufferSize = new( bufferWidth, 50 );

    /// <inheritdoc/>
    public override Size BufferSize {
        get => _bufferSize;
        set { /* no-op — headless host */ }
    }

    /// <inheritdoc/>
    public override Size WindowSize {
        get => _bufferSize;
        set { /* no-op */ }
    }

    /// <inheritdoc/>
    public override Size MaxWindowSize => _bufferSize;

    /// <inheritdoc/>
    public override Size MaxPhysicalWindowSize => _bufferSize;

    /// <inheritdoc/>
    public override Coordinates WindowPosition {
        get => new( 0, 0 );
        set { /* no-op */ }
    }

    /// <inheritdoc/>
    public override Coordinates CursorPosition {
        get => new( 0, 0 );
        set { /* no-op */ }
    }

    /// <inheritdoc/>
    public override int CursorSize {
        get => 25;
        set { /* no-op */ }
    }

    /// <inheritdoc/>
    public override ConsoleColor ForegroundColor {
        get => ConsoleColor.White;
        set { /* no-op */ }
    }

    /// <inheritdoc/>
    public override ConsoleColor BackgroundColor {
        get => ConsoleColor.Black;
        set { /* no-op */ }
    }

    /// <inheritdoc/>
    public override string WindowTitle {
        get => "Werkr Agent";
        set { /* no-op */ }
    }

    /// <inheritdoc/>
    public override bool KeyAvailable => false;

    /// <inheritdoc/>
    public override KeyInfo ReadKey( ReadKeyOptions options )
        => throw new NotSupportedException( "Non-interactive host." );

    /// <inheritdoc/>
    public override void FlushInputBuffer( ) { /* no-op */ }

    /// <inheritdoc/>
    public override void SetBufferContents( Coordinates origin, BufferCell[,] contents )
        => throw new NotSupportedException( "Non-interactive host." );

    /// <inheritdoc/>
    public override void SetBufferContents( Rectangle rectangle, BufferCell fill )
        => throw new NotSupportedException( "Non-interactive host." );

    /// <inheritdoc/>
    public override BufferCell[,] GetBufferContents( Rectangle rectangle )
        => throw new NotSupportedException( "Non-interactive host." );

    /// <inheritdoc/>
    public override void ScrollBufferContents(
        Rectangle source, Coordinates destination, Rectangle clip, BufferCell fill )
        => throw new NotSupportedException( "Non-interactive host." );
}
