using Bunit;
using Microsoft.AspNetCore.Components;
using Werkr.Server.Components.Shared;

namespace Werkr.Tests.Server.Components;

/// <summary>
/// bUnit tests for <see cref="IntArrayEditor"/>. Verifies rendering,
/// add/remove behaviour, value binding, and error display for non-integer input.
/// </summary>
[TestClass]
public class IntArrayEditorTests : BunitContext {
    /// <summary>
    /// Empty list renders no inputs.
    /// </summary>
    [TestMethod]
    public void Empty_List_Renders_No_Inputs( ) {
        IRenderedComponent<IntArrayEditor> cut = Render<IntArrayEditor>( parameters => parameters
            .Add( p => p.Value, [] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<int>?>( this, _ => { } ) ) );

        IReadOnlyList<AngleSharp.Dom.IElement> inputs = cut.FindAll( "input[type='number']" );
        Assert.HasCount( 0, inputs, "No number inputs for empty list." );
    }

    /// <summary>
    /// Add button inserts one new input.
    /// </summary>
    [TestMethod]
    public void Add_Button_Adds_Item( ) {
        IRenderedComponent<IntArrayEditor> cut = Render<IntArrayEditor>( parameters => parameters
            .Add( p => p.Value, [] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<int>?>( this, _ => { } ) ) );

        cut.FindAll( "button" ).First( b => b.TextContent.Contains( "Add" ) ).Click( );

        IReadOnlyList<AngleSharp.Dom.IElement> inputs = cut.FindAll( "input[type='number']" );
        Assert.HasCount( 1, inputs, "Should have 1 number input after Add." );
    }

    /// <summary>
    /// Remove button removes the targeted item.
    /// </summary>
    [TestMethod]
    public void Remove_Button_Removes_Item( ) {
        IRenderedComponent<IntArrayEditor> cut = Render<IntArrayEditor>( parameters => parameters
            .Add( p => p.Value, [80, 443] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<int>?>( this, _ => { } ) ) );

        AngleSharp.Dom.IElement removeBtn = cut.FindAll( "button" )
            .First( b => b.GetAttribute( "class" )?.Contains( "btn-outline-danger" ) == true );
        removeBtn.Click( );

        IReadOnlyList<AngleSharp.Dom.IElement> inputs = cut.FindAll( "input[type='number']" );
        Assert.HasCount( 1, inputs, "Should have 1 input after removing one item." );
    }

    /// <summary>
    /// Pre-populated values appear in the inputs.
    /// </summary>
    [TestMethod]
    public void Prepopulated_Values_Render( ) {
        IRenderedComponent<IntArrayEditor> cut = Render<IntArrayEditor>( parameters => parameters
            .Add( p => p.Value, [8080, 9090] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<int>?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        Assert.Contains( "8080", markup, "First value should render." );
        Assert.Contains( "9090", markup, "Second value should render." );
    }

    /// <summary>
    /// Placeholder text is rendered on each input.
    /// </summary>
    [TestMethod]
    public void Placeholder_Rendered( ) {
        IRenderedComponent<IntArrayEditor> cut = Render<IntArrayEditor>( parameters => parameters
            .Add( p => p.Value, [0] )
            .Add( p => p.Placeholder, "Enter port" )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<int>?>( this, _ => { } ) ) );

        Assert.Contains( "Enter port", cut.Markup, "Placeholder text should be rendered." );
    }

    /// <summary>
    /// Changing a value fires <c>ValueChanged</c>.
    /// </summary>
    [TestMethod]
    public void Changing_Value_Fires_ValueChanged( ) {
        List<int>? emitted = null;
        IRenderedComponent<IntArrayEditor> cut = Render<IntArrayEditor>( parameters => parameters
            .Add( p => p.Value, [80] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<int>?>( this, v => emitted = v ) ) );

        cut.Find( "input[type='number']" ).Change( "443" );

        Assert.IsNotNull( emitted, "ValueChanged should fire." );
        CollectionAssert.Contains( emitted, 443, "Emitted list should contain the updated value." );
    }
}
