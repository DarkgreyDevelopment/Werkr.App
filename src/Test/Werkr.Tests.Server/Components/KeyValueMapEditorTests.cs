using Bunit;
using Microsoft.AspNetCore.Components;
using Werkr.Server.Components.Shared;

namespace Werkr.Tests.Server.Components;

/// <summary>
/// bUnit tests for <see cref="KeyValueMapEditor"/>. Verifies rendering,
/// add/remove, duplicate key detection, and value binding.
/// </summary>
[TestClass]
public class KeyValueMapEditorTests : BunitContext {
    /// <summary>
    /// Empty dictionary renders no key-value rows.
    /// </summary>
    [TestMethod]
    public void Empty_Map_Renders_Add_Button_Only( ) {
        IRenderedComponent<KeyValueMapEditor> cut = Render<KeyValueMapEditor>( parameters => parameters
            .Add( p => p.Value, [] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<Dictionary<string, string>?>( this, _ => { } ) ) );

        IReadOnlyList<AngleSharp.Dom.IElement> inputs = cut.FindAll( "input[type='text']" );
        Assert.HasCount( 0, inputs, "No text inputs for an empty map." );
    }

    /// <summary>
    /// Add button creates a new key-value pair row.
    /// </summary>
    [TestMethod]
    public void Add_Button_Adds_Row( ) {
        IRenderedComponent<KeyValueMapEditor> cut = Render<KeyValueMapEditor>( parameters => parameters
            .Add( p => p.Value, [] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<Dictionary<string, string>?>( this, _ => { } ) ) );

        cut.FindAll( "button" ).First( b => b.TextContent.Contains( "Add" ) ).Click( );

        // Should have at least 2 text inputs (key + value)
        IReadOnlyList<AngleSharp.Dom.IElement> inputs = cut.FindAll( "input[type='text']" );
        Assert.IsGreaterThanOrEqualTo( 2, inputs.Count, "Should have key and value inputs after Add." );
    }

    /// <summary>
    /// Remove button removes a row.
    /// </summary>
    [TestMethod]
    public void Remove_Button_Removes_Row( ) {
        IRenderedComponent<KeyValueMapEditor> cut = Render<KeyValueMapEditor>( parameters => parameters
            .Add( p => p.Value, new Dictionary<string, string> { ["Accept"] = "text/html", ["Cache"] = "no-cache" } )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<Dictionary<string, string>?>( this, _ => { } ) ) );

        AngleSharp.Dom.IElement removeBtn = cut.FindAll( "button" )
            .First( b => b.GetAttribute( "class" )?.Contains( "btn-outline-danger" ) == true );
        removeBtn.Click( );

        // After removing one row, there should be one fewer pair (2 inputs = 1 key + 1 value)
        IReadOnlyList<AngleSharp.Dom.IElement> inputs = cut.FindAll( "input[type='text']" );
        Assert.HasCount( 2, inputs, "Should have 2 text inputs (1 key + 1 value) after removing one row." );
    }

    /// <summary>
    /// Pre-populated map renders key-value values.
    /// </summary>
    [TestMethod]
    public void Prepopulated_Map_Renders_Values( ) {
        IRenderedComponent<KeyValueMapEditor> cut = Render<KeyValueMapEditor>( parameters => parameters
            .Add( p => p.Value, new Dictionary<string, string> { ["Authorization"] = "Bearer tok" } )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<Dictionary<string, string>?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        Assert.Contains( "Authorization", markup, "Key should be rendered." );
        Assert.Contains( "Bearer tok", markup, "Value should be rendered." );
    }

    /// <summary>
    /// <see cref="KeyValueMapEditor.HasDuplicates"/> returns false for a single entry.
    /// </summary>
    [TestMethod]
    public void HasDuplicates_False_For_Single_Entry( ) {
        IRenderedComponent<KeyValueMapEditor> cut = Render<KeyValueMapEditor>( parameters => parameters
            .Add( p => p.Value, new Dictionary<string, string> { ["X-Key"] = "val" } )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<Dictionary<string, string>?>( this, _ => { } ) ) );

        Assert.IsFalse( cut.Instance.HasDuplicates, "No duplicates with a single entry." );
    }

    /// <summary>
    /// Changing a key fires <c>ValueChanged</c>.
    /// </summary>
    [TestMethod]
    public void Changing_Key_Fires_ValueChanged( ) {
        Dictionary<string, string>? emitted = null;
        IRenderedComponent<KeyValueMapEditor> cut = Render<KeyValueMapEditor>( parameters => parameters
            .Add( p => p.Value, new Dictionary<string, string> { ["OldKey"] = "val" } )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<Dictionary<string, string>?>( this, v => emitted = v ) ) );

        // Change the first key input (Key placeholder)
        IReadOnlyList<AngleSharp.Dom.IElement> inputs = cut.FindAll( "input[type='text']" );
        inputs[0].Change( "NewKey" );

        Assert.IsNotNull( emitted, "ValueChanged should fire on key change." );
        Assert.IsTrue( emitted.ContainsKey( "NewKey" ), "Emitted dictionary should contain the updated key." );
    }

    /// <summary>
    /// The "+ Add Pair" button text is rendered correctly.
    /// </summary>
    [TestMethod]
    public void Add_Pair_Button_Text( ) {
        IRenderedComponent<KeyValueMapEditor> cut = Render<KeyValueMapEditor>( parameters => parameters
            .Add( p => p.Value, [] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<Dictionary<string, string>?>( this, _ => { } ) ) );

        Assert.IsNotNull(
            cut.FindAll( "button" ).FirstOrDefault( b => b.TextContent.Contains( "Add Pair" ) ),
            "Should render '+ Add Pair' button." );
    }
}
