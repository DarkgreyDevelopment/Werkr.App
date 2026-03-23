using Bunit;
using Microsoft.AspNetCore.Components;
using Werkr.Server.Components.Shared;

namespace Werkr.Tests.Server.Components;

/// <summary>
/// bUnit tests for <see cref="StringArrayEditor"/>. Verifies rendering,
/// add/remove behaviour, value binding, and placeholder display.
/// </summary>
[TestClass]
public class StringArrayEditorTests : BunitContext {
    /// <summary>
    /// Rendering with an empty list shows no item inputs, only the Add button.
    /// </summary>
    [TestMethod]
    public void Empty_List_Renders_Add_Button_Only( ) {
        IRenderedComponent<StringArrayEditor> cut = Render<StringArrayEditor>( parameters => parameters
            .Add( p => p.Value, [] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<string>?>( this, _ => { } ) ) );

        IReadOnlyList<AngleSharp.Dom.IElement> inputs = cut.FindAll( "input[type='text']" );
        Assert.HasCount( 0, inputs, "Should have no text inputs for an empty list." );
        Assert.IsNotNull(
            cut.FindAll( "button" ).FirstOrDefault( b => b.TextContent.Contains( "Add" ) ),
            "Add button should be present." );
    }

    /// <summary>
    /// Clicking the Add button adds one more input.
    /// </summary>
    [TestMethod]
    public void Add_Button_Adds_Item( ) {
        List<string>? emitted = null;
        IRenderedComponent<StringArrayEditor> cut = Render<StringArrayEditor>( parameters => parameters
            .Add( p => p.Value, [] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<string>?>( this, v => emitted = v ) ) );

        cut.FindAll( "button" ).First( b => b.TextContent.Contains( "Add" ) ).Click( );

        IReadOnlyList<AngleSharp.Dom.IElement> inputs = cut.FindAll( "input[type='text']" );
        Assert.HasCount( 1, inputs, "Should have 1 input after clicking Add." );
    }

    /// <summary>
    /// Clicking the Remove button removes the item.
    /// </summary>
    [TestMethod]
    public void Remove_Button_Removes_Item( ) {
        IRenderedComponent<StringArrayEditor> cut = Render<StringArrayEditor>( parameters => parameters
            .Add( p => p.Value, ["one", "two"] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<string>?>( this, _ => { } ) ) );

        // Click the first remove button (×)
        AngleSharp.Dom.IElement removeBtn = cut.FindAll( "button" )
            .First( b => b.GetAttribute( "class" )?.Contains( "btn-outline-danger" ) == true );
        removeBtn.Click( );

        IReadOnlyList<AngleSharp.Dom.IElement> inputs = cut.FindAll( "input[type='text']" );
        Assert.HasCount( 1, inputs, "Should have 1 input after removing one item." );
    }

    /// <summary>
    /// Pre-populated items render their values.
    /// </summary>
    [TestMethod]
    public void Prepopulated_Items_Render_Values( ) {
        IRenderedComponent<StringArrayEditor> cut = Render<StringArrayEditor>( parameters => parameters
            .Add( p => p.Value, ["alpha", "beta"] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<string>?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        Assert.Contains( "alpha", markup, "First item value should appear." );
        Assert.Contains( "beta", markup, "Second item value should appear." );
    }

    /// <summary>
    /// Placeholder text is rendered on each input.
    /// </summary>
    [TestMethod]
    public void Placeholder_Rendered( ) {
        IRenderedComponent<StringArrayEditor> cut = Render<StringArrayEditor>( parameters => parameters
            .Add( p => p.Value, [""] )
            .Add( p => p.Placeholder, "Enter email" )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<string>?>( this, _ => { } ) ) );

        Assert.Contains( "Enter email", cut.Markup, "Placeholder text should be rendered." );
    }

    /// <summary>
    /// Changing an item value fires <c>ValueChanged</c>.
    /// </summary>
    [TestMethod]
    public void Changing_Item_Fires_ValueChanged( ) {
        List<string>? emitted = null;
        IRenderedComponent<StringArrayEditor> cut = Render<StringArrayEditor>( parameters => parameters
            .Add( p => p.Value, ["old"] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<string>?>( this, v => emitted = v ) ) );

        cut.Find( "input[type='text']" ).Change( "new-value" );

        Assert.IsNotNull( emitted, "ValueChanged should fire on item change." );
        CollectionAssert.Contains( emitted, "new-value", "Emitted list should contain the updated value." );
    }
}
