using Bunit;
using Microsoft.AspNetCore.Components;
using Werkr.Common.Models.Actions;
using Werkr.Server.Components.Shared;

namespace Werkr.Tests.Server.Components;

/// <summary>
/// bUnit tests for <see cref="ObjectArrayEditor"/>. Verifies rendering of sub-field
/// columns, add/remove rows, value binding, and the unsupported-type fallback.
/// </summary>
[TestClass]
public class ObjectArrayEditorTests : BunitContext {
    // ── Rendering ───────────────────────────────────────────────────

    /// <summary>
    /// Empty list shows the Add button but no card bodies.
    /// </summary>
    [TestMethod]
    public void Empty_List_Renders_Add_Button_Only( ) {
        IRenderedComponent<ObjectArrayEditor> cut = Render<ObjectArrayEditor>( parameters => parameters
            .Add( p => p.SubFields, GetTransformJsonSubFields( ) )
            .Add( p => p.Value, [] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<Dictionary<string, object?>>?>( this, _ => { } ) ) );

        IReadOnlyList<AngleSharp.Dom.IElement> bodies = cut.FindAll( ".card-body" );
        Assert.HasCount( 0, bodies, "No card bodies for an empty list." );
    }

    /// <summary>
    /// Add button creates one row with sub-field inputs.
    /// </summary>
    [TestMethod]
    public void Add_Button_Adds_Row( ) {
        List<Dictionary<string, object?>>? emitted = null;
        IRenderedComponent<ObjectArrayEditor> cut = Render<ObjectArrayEditor>( parameters => parameters
            .Add( p => p.SubFields, GetTransformJsonSubFields( ) )
            .Add( p => p.Value, [] )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<Dictionary<string, object?>>?>( this, v => emitted = v ) ) );

        cut.FindAll( "button" ).First( b => b.TextContent.Contains( "Add" ) ).Click( );

        Assert.IsGreaterThan( 0, cut.FindAll( ".card" ).Count, "Should have at least one card after Add." );
    }

    /// <summary>
    /// Remove button removes the targeted row.
    /// </summary>
    [TestMethod]
    public void Remove_Button_Removes_Row( ) {
        List<Dictionary<string, object?>> items = [
            new( ) { ["type"] = "Set", ["path"] = "/a", ["value"] = "x" },
            new( ) { ["type"] = "Remove", ["path"] = "/b" },
        ];

        IRenderedComponent<ObjectArrayEditor> cut = Render<ObjectArrayEditor>( parameters => parameters
            .Add( p => p.SubFields, GetTransformJsonSubFields( ) )
            .Add( p => p.Value, items )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<Dictionary<string, object?>>?>( this, _ => { } ) ) );

        // Click the first remove button
        AngleSharp.Dom.IElement removeBtn = cut.FindAll( "button" )
            .First( b => b.GetAttribute( "class" )?.Contains( "btn-outline-danger" ) == true );
        removeBtn.Click( );

        Assert.HasCount( 1, cut.FindAll( ".card" ), "Should have 1 card after removing one." );
    }

    /// <summary>
    /// Pre-populated items render their sub-field values.
    /// </summary>
    [TestMethod]
    public void Prepopulated_Items_Render_SubField_Values( ) {
        List<Dictionary<string, object?>> items = [
            new( ) { ["type"] = "Set", ["path"] = "/name", ["value"] = "John" },
        ];

        IRenderedComponent<ObjectArrayEditor> cut = Render<ObjectArrayEditor>( parameters => parameters
            .Add( p => p.SubFields, GetTransformJsonSubFields( ) )
            .Add( p => p.Value, items )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<Dictionary<string, object?>>?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        Assert.Contains( "/name", markup, "Path sub-field value should render." );
    }

    /// <summary>
    /// Changing a sub-field value fires <c>ValueChanged</c>.
    /// </summary>
    [TestMethod]
    public void Changing_SubField_Fires_ValueChanged( ) {
        List<Dictionary<string, object?>>? emitted = null;
        List<Dictionary<string, object?>> items = [
            new( ) { ["type"] = "Set", ["path"] = "/old", ["value"] = "v" },
        ];

        IRenderedComponent<ObjectArrayEditor> cut = Render<ObjectArrayEditor>( parameters => parameters
            .Add( p => p.SubFields, GetTransformJsonSubFields( ) )
            .Add( p => p.Value, items )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<Dictionary<string, object?>>?>( this, v => emitted = v ) ) );

        // Change the first text input
        IReadOnlyList<AngleSharp.Dom.IElement> inputs = cut.FindAll( "input[type='text']" );
        if (inputs.Count > 0) {
            inputs[0].Change( "/updated" );
        }

        Assert.IsNotNull( emitted, "ValueChanged should fire on sub-field change." );
    }

    // ── Sub-field types ─────────────────────────────────────────────

    /// <summary>
    /// Boolean sub-fields render checkboxes.
    /// </summary>
    [TestMethod]
    public void Bool_SubField_Renders_Checkbox( ) {
        List<FieldDescriptor> subFields = [
            new( "Active", "Active", FieldType.Bool ),
        ];
        List<Dictionary<string, object?>> items = [
            new( ) { ["active"] = true },
        ];

        IRenderedComponent<ObjectArrayEditor> cut = Render<ObjectArrayEditor>( parameters => parameters
            .Add( p => p.SubFields, subFields )
            .Add( p => p.Value, items )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<Dictionary<string, object?>>?>( this, _ => { } ) ) );

        IReadOnlyList<AngleSharp.Dom.IElement> checkboxes = cut.FindAll( "input[type='checkbox']" );
        Assert.IsGreaterThanOrEqualTo( 1, checkboxes.Count, "Bool sub-field should render a checkbox." );
    }

    /// <summary>
    /// Number sub-fields render number inputs.
    /// </summary>
    [TestMethod]
    public void Number_SubField_Renders_Number_Input( ) {
        List<FieldDescriptor> subFields = [
            new( "Count", "Count", FieldType.Number ),
        ];
        List<Dictionary<string, object?>> items = [
            new( ) { ["count"] = 5 },
        ];

        IRenderedComponent<ObjectArrayEditor> cut = Render<ObjectArrayEditor>( parameters => parameters
            .Add( p => p.SubFields, subFields )
            .Add( p => p.Value, items )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<Dictionary<string, object?>>?>( this, _ => { } ) ) );

        IReadOnlyList<AngleSharp.Dom.IElement> numberInputs = cut.FindAll( "input[type='number']" );
        Assert.IsGreaterThanOrEqualTo( 1, numberInputs.Count, "Number sub-field should render a number input." );
    }

    /// <summary>
    /// Select sub-fields render select elements with options.
    /// </summary>
    [TestMethod]
    public void Select_SubField_Renders_Select_Element( ) {
        List<FieldDescriptor> subFields = [
            new( "Type", "Operation Type", FieldType.Select, Options: ["Set", "Remove", "Replace"] ),
        ];
        List<Dictionary<string, object?>> items = [
            new( ) { ["type"] = "Set" },
        ];

        IRenderedComponent<ObjectArrayEditor> cut = Render<ObjectArrayEditor>( parameters => parameters
            .Add( p => p.SubFields, subFields )
            .Add( p => p.Value, items )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<Dictionary<string, object?>>?>( this, _ => { } ) ) );

        IReadOnlyList<AngleSharp.Dom.IElement> selects = cut.FindAll( "select" );
        Assert.IsGreaterThanOrEqualTo( 1, selects.Count, "Select sub-field should render a <select> element." );
    }

    /// <summary>
    /// Text sub-fields render text inputs with their labels.
    /// </summary>
    [TestMethod]
    public void Text_SubField_Renders_Text_Input( ) {
        List<FieldDescriptor> subFields = [
            new( "ExtractTo", "Extract To", FieldType.Text ),
        ];
        List<Dictionary<string, object?>> items = [
            new( ) { ["extractTo"] = "/out" },
        ];

        IRenderedComponent<ObjectArrayEditor> cut = Render<ObjectArrayEditor>( parameters => parameters
            .Add( p => p.SubFields, subFields )
            .Add( p => p.Value, items )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<Dictionary<string, object?>>?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        Assert.Contains(
            "Extract To",
            markup,
            "Text sub-field label should be rendered." );
    }

    /// <summary>
    /// Collapsing a row hides the card body.
    /// </summary>
    [TestMethod]
    public void Collapse_Toggle_Hides_Card_Body( ) {
        List<Dictionary<string, object?>> items = [
            new( ) { ["type"] = "Set", ["path"] = "/x", ["value"] = "y" },
        ];

        IRenderedComponent<ObjectArrayEditor> cut = Render<ObjectArrayEditor>( parameters => parameters
            .Add( p => p.SubFields, GetTransformJsonSubFields( ) )
            .Add( p => p.Value, items )
            .Add( p => p.ValueChanged, EventCallback.Factory.Create<List<Dictionary<string, object?>>?>( this, _ => { } ) ) );

        // Initially expanded — card-body should exist
        Assert.IsGreaterThan( 0, cut.FindAll( ".card-body" ).Count, "Card body should be visible initially." );

        // Click the collapse toggle (btn-link in card-header)
        AngleSharp.Dom.IElement toggleBtn = cut.FindAll( "button" )
            .First( b => b.GetAttribute( "class" )?.Contains( "btn-link" ) == true );
        toggleBtn.Click( );

        // After collapse, card-body should not be present
        Assert.HasCount( 0, cut.FindAll( ".card-body" ), "Card body should be hidden after collapse." );
    }

    // ── Helper ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the sub-fields for TransformJson's Operations parameter.
    /// </summary>
    private static List<FieldDescriptor> GetTransformJsonSubFields( ) {
        return [
            new( "Type", "Operation Type", FieldType.Select, Required: true, Options: ["Set", "Remove", "Replace", "Move", "Copy", "Test"] ),
            new( "Path", "JSON Pointer Path", FieldType.Text, Required: true ),
            new( "Value", "Value", FieldType.Text ),
        ];
    }
}
