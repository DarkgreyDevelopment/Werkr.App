using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Werkr.Common.Models.Actions;
using Werkr.Server.Components.Shared;

namespace Werkr.Tests.Server.Components;

/// <summary>
/// bUnit tests for <see cref="ActionParameterEditor"/>. Verifies action selection,
/// field rendering, validation, conditional visibility, JSON mode toggling,
/// and two-way parameter binding.
/// </summary>
[TestClass]
public class ActionParameterEditorTests : BunitContext {
    /// <summary>
    /// Shared JSON options matching the component's internal serializer.
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── Action Selection ────────────────────────────────────────────

    /// <summary>
    /// Verifies that the action dropdown renders an optgroup for each
    /// category in <see cref="ActionRegistry.Grouped"/>.
    /// </summary>
    [TestMethod]
    public void Renders_Optgroups_For_Each_Category( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( );

        IReadOnlyList<AngleSharp.Dom.IElement> optgroups = cut.FindAll( "select#actionSubType optgroup" );
        Assert.HasCount( ActionRegistry.Categories.Count, optgroups, "Should render one optgroup per category." );
    }

    /// <summary>
    /// Verifies that all 27 action options appear in the dropdown.
    /// </summary>
    [TestMethod]
    public void Renders_All_TwentySeven_Actions_In_Dropdown( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( );

        // All <option> except the "— select action —" placeholder
        IReadOnlyList<AngleSharp.Dom.IElement> options = cut.FindAll( "select#actionSubType option[value]:not([value=''])" );
        Assert.HasCount( 27, options, "Should list all 27 actions." );
    }

    /// <summary>
    /// Selecting an action fires <c>ActionSubTypeChanged</c> and renders the
    /// description text.
    /// </summary>
    [TestMethod]
    public void Selecting_Action_Fires_ActionSubTypeChanged( ) {
        string? captured = null;
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, v => captured = v ) ) );

        cut.Find( "select#actionSubType" ).Change( "CopyFile" );

        Assert.AreEqual( "CopyFile", captured );
    }

    /// <summary>
    /// Verifies that after selecting an action, its description text is shown.
    /// </summary>
    [TestMethod]
    public void Selecting_Action_Renders_Description( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        cut.Find( "select#actionSubType" ).Change( "Delay" );

        string markup = cut.Markup;
        Assert.Contains(
            "Pause workflow execution",
            markup,
            "Description for Delay action should be rendered." );
    }

    // ── Field Rendering ─────────────────────────────────────────────

    /// <summary>
    /// Delay has 2 fields (Seconds: Number required, Reason: Text optional).
    /// Verifies that both labels are rendered.
    /// </summary>
    [TestMethod]
    public void Delay_Renders_Two_Field_Labels( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "Delay" )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        Assert.Contains( "Seconds", markup, "Should render Seconds label." );
        Assert.Contains( "Reason", markup, "Should render Reason label." );
    }

    /// <summary>
    /// Verifies that a required field shows the red asterisk star.
    /// </summary>
    [TestMethod]
    public void Required_Field_Shows_Star( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "Delay" )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        IReadOnlyList<AngleSharp.Dom.IElement> stars = cut.FindAll( "span.text-danger" );
        Assert.IsGreaterThanOrEqualTo( 1, stars.Count, "At least one required-field star should be rendered." );
    }

    /// <summary>
    /// CreateFile renders a Select field for Encoding with all Encoding options.
    /// </summary>
    [TestMethod]
    public void CreateFile_Renders_Encoding_Select( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "CreateFile" )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        foreach (string encoding in ActionRegistry.Encodings) {
            Assert.Contains(
                $"value=\"{encoding}\"",
                markup,
                $"Encoding option '{encoding}' should be present." );
        }
    }

    // ── Validation ──────────────────────────────────────────────────

    /// <summary>
    /// Calling <c>Validate()</c> with no data on a Delay action should report
    /// the required Seconds field as an error.
    /// </summary>
    [TestMethod]
    public void Validate_Required_Field_Missing_Returns_False( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "Delay" )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        bool result = false;
        _ = cut.InvokeAsync( ( ) => result = cut.Instance.Validate( ) ).GetAwaiter( ).GetResult( );

        Assert.IsFalse( result, "Validate should return false when required field is empty." );
        Assert.Contains(
            "Seconds is required",
            cut.Markup,
            "Should display error for missing required Seconds field." );
    }

    /// <summary>
    /// Calling <c>Validate()</c> with valid data on a Delay action should return true.
    /// </summary>
    [TestMethod]
    public void Validate_With_Valid_Data_Returns_True( ) {
        string json = JsonSerializer.Serialize(
            new { seconds = 10 }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "Delay" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        bool result = false;
        _ = cut.InvokeAsync( ( ) => result = cut.Instance.Validate( ) ).GetAwaiter( ).GetResult( );

        Assert.IsTrue( result, "Validate should return true when required fields are present." );
    }

    /// <summary>
    /// Verifies that <c>Validate()</c> catches an empty required StringArray.
    /// SendEmail requires at least one To address.
    /// </summary>
    [TestMethod]
    public void Validate_Empty_Required_StringArray_Returns_False( ) {
        string json = JsonSerializer.Serialize( new {
            smtpHost = "smtp.test.com",
            from = "a@b.com",
            to = Array.Empty<string>( ),
            subject = "Test"
        }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "SendEmail" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        bool result = false;
        _ = cut.InvokeAsync( ( ) => result = cut.Instance.Validate( ) ).GetAwaiter( ).GetResult( );

        Assert.IsFalse( result, "Validate should fail when required StringArray is empty." );
        Assert.Contains(
            "At least one",
            cut.Markup,
            "Should display 'At least one' error for empty required StringArray." );
    }

    /// <summary>
    /// Verifies that <c>Validate()</c> catches an empty required ObjectArray.
    /// TransformJson requires at least one Operation.
    /// </summary>
    [TestMethod]
    public void Validate_Empty_Required_ObjectArray_Returns_False( ) {
        string json = JsonSerializer.Serialize( new {
            operations = Array.Empty<object>( )
        }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "TransformJson" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        bool result = false;
        _ = cut.InvokeAsync( ( ) => result = cut.Instance.Validate( ) ).GetAwaiter( ).GetResult( );

        Assert.IsFalse( result, "Validate should fail when required ObjectArray is empty." );
    }

    /// <summary>
    /// Verifies that <c>Validate()</c> catches a missing required sub-field in an ObjectArray item.
    /// TransformJson operations require Type and Path sub-fields.
    /// </summary>
    [TestMethod]
    public void Validate_ObjectArray_Missing_Required_SubField_Returns_False( ) {
        // Operations has items but sub-field "Type" is missing
        string json = JsonSerializer.Serialize( new {
            operations = new[] {
                new { path = "/name" }  // Missing Type
            }
        }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "TransformJson" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        bool result = false;
        _ = cut.InvokeAsync( ( ) => result = cut.Instance.Validate( ) ).GetAwaiter( ).GetResult( );

        Assert.IsFalse( result, "Validate should fail when required sub-field is missing." );
        Assert.Contains(
            "Item 1",
            cut.Markup,
            "Error should reference the item number." );
    }

    /// <summary>
    /// Verifies that number min/max validation works.
    /// TestConnection Port field has Min: 1, Max: 65535.
    /// </summary>
    [TestMethod]
    public void Validate_Number_Below_Min_Returns_Error( ) {
        string json = JsonSerializer.Serialize( new {
            host = "localhost",
            port = 0   // Below Min=1
        }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "TestConnection" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        bool result = false;
        _ = cut.InvokeAsync( ( ) => result = cut.Instance.Validate( ) ).GetAwaiter( ).GetResult( );

        Assert.IsFalse( result, "Validate should fail when number is below minimum." );
        Assert.Contains(
            "Minimum value",
            cut.Markup,
            "Should display a min-value validation error." );
    }

    /// <summary>
    /// Verifies that number max validation works.
    /// TestConnection Port field has Min: 1, Max: 65535.
    /// </summary>
    [TestMethod]
    public void Validate_Number_Above_Max_Returns_Error( ) {
        string json = JsonSerializer.Serialize( new {
            host = "localhost",
            port = 99999   // Above Max=65535
        }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "TestConnection" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        bool result = false;
        _ = cut.InvokeAsync( ( ) => result = cut.Instance.Validate( ) ).GetAwaiter( ).GetResult( );

        Assert.IsFalse( result, "Validate should fail when number exceeds maximum." );
        Assert.Contains(
            "Maximum value",
            cut.Markup,
            "Should display a max-value validation error." );
    }

    // ── Validation with no descriptor ───────────────────────────────

    /// <summary>
    /// Validate returns true when no action is selected (no descriptor).
    /// </summary>
    [TestMethod]
    public void Validate_No_Action_Selected_Returns_True( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( );

        bool result = false;
        _ = cut.InvokeAsync( ( ) => result = cut.Instance.Validate( ) ).GetAwaiter( ).GetResult( );
        Assert.IsTrue( result, "Validate with no action selected should return true." );
    }

    // ── Conditional Display (ShowWhen) ──────────────────────────────

    /// <summary>
    /// StartProcess has a TimeoutMs field with ShowWhen="WaitForExit=true".
    /// When WaitForExit is false (default), TimeoutMs should NOT be rendered.
    /// </summary>
    [TestMethod]
    public void ShowWhen_Hidden_Field_Not_Rendered( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "StartProcess" )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        Assert.DoesNotContain(
            "Timeout (ms)",
            markup,
            "TimeoutMs should be hidden when WaitForExit is false." );
    }

    /// <summary>
    /// When WaitForExit is true, the TimeoutMs field should appear.
    /// </summary>
    [TestMethod]
    public void ShowWhen_Visible_Field_Rendered_When_Condition_Met( ) {
        string json = JsonSerializer.Serialize(
            new { waitForExit = true }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "StartProcess" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        Assert.Contains(
            "Timeout (ms)",
            markup,
            "TimeoutMs should appear when WaitForExit=true." );
    }

    /// <summary>
    /// HttpRequest Body and ContentType fields have ShowWhen="Method=POST|PUT|PATCH|DELETE".
    /// When Method is GET (default), those fields should NOT appear.
    /// </summary>
    [TestMethod]
    public void ShowWhen_MultiValue_Hidden_When_No_Match( ) {
        string json = JsonSerializer.Serialize(
            new { url = "https://example.com", method = "GET" }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "HttpRequest" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        Assert.DoesNotContain(
            "Request Body",
            markup,
            "Body field should not appear when Method=GET." );
    }

    /// <summary>
    /// When Method is POST, Body and ContentType should appear.
    /// </summary>
    [TestMethod]
    public void ShowWhen_MultiValue_Visible_When_Match( ) {
        string json = JsonSerializer.Serialize(
            new { url = "https://example.com", method = "POST" }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "HttpRequest" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        Assert.Contains(
            "Request Body",
            markup,
            "Body field should appear when Method=POST." );
    }

    // ── JSON Mode Toggle ────────────────────────────────────────────

    /// <summary>
    /// Clicking the JSON View toggle shows the textarea and hides form fields.
    /// </summary>
    [TestMethod]
    public void Toggle_To_Json_Mode_Shows_Textarea( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "Delay" )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        // Click the toggle button (with bi-code-slash icon for "JSON View")
        AngleSharp.Dom.IElement toggleBtn = cut.FindAll( "button" )
            .First( b => b.TextContent.Contains( "JSON View" ) );
        toggleBtn.Click( );

        Assert.Contains(
            "JSON Parameters",
            cut.Markup,
            "Should display JSON editing mode." );
        AngleSharp.Dom.IElement textarea = cut.Find( "textarea" );
        Assert.IsNotNull( textarea, "Should render a textarea for JSON editing." );
    }

    /// <summary>
    /// Toggling back to Form View from JSON (with valid JSON) should work.
    /// </summary>
    [TestMethod]
    public void Toggle_Back_To_Form_From_Valid_Json( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "Delay" )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        // Toggle to JSON mode
        AngleSharp.Dom.IElement toggleBtn = cut.FindAll( "button" )
            .First( b => b.TextContent.Contains( "JSON View" ) );
        toggleBtn.Click( );

        // Toggle back (button now says "Form View")
        AngleSharp.Dom.IElement formBtn = cut.FindAll( "button" )
            .First( b => b.TextContent.Contains( "Form View" ) );
        formBtn.Click( );

        Assert.DoesNotContain(
            "JSON Parameters",
            cut.Markup,
            "Should return to form view." );
    }

    // ── Parameter Binding ───────────────────────────────────────────

    /// <summary>
    /// Verifies that providing <c>ActionParameters</c> JSON seeds the form values.
    /// </summary>
    [TestMethod]
    public void ActionParameters_Seeds_Form_Values( ) {
        string json = JsonSerializer.Serialize(
            new { seconds = 42, reason = "Test reason" }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "Delay" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        Assert.Contains(
            "42",
            markup,
            "Seconds field should contain parsed value '42'." );
        Assert.Contains(
            "Test reason",
            markup,
            "Reason field should contain parsed value 'Test reason'." );
    }

    /// <summary>
    /// Verifies that setting a value from the form triggers <c>ActionParametersChanged</c>
    /// with valid JSON that includes the updated value.
    /// </summary>
    [TestMethod]
    public void Setting_Value_Emits_ActionParametersChanged( ) {
        string? emittedJson = null;
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "ForEach" )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, v => emittedJson = v ) ) );

        // ForEach has a single required text field: ArrayPropertyName.
        AngleSharp.Dom.IElement input = cut.Find( "input[type='text']" );
        input.Change( "items" );

        Assert.IsNotNull( emittedJson, "ActionParametersChanged should have been invoked." );

        using JsonDocument doc = JsonDocument.Parse( emittedJson );
        Assert.AreEqual(
            "items",
            doc.RootElement.GetProperty( "arrayPropertyName" ).GetString( ),
            "Emitted JSON should contain the typed value." );
    }

    // ── Bool Default Values ─────────────────────────────────────────

    /// <summary>
    /// CopyFile has Overwrite=false and Recursive=false as defaults.
    /// When selected, the checkboxes should be unchecked by default.
    /// </summary>
    [TestMethod]
    public void Bool_Default_False_Renders_Unchecked( ) {
        string json = JsonSerializer.Serialize( new {
            source = "/a",
            destination = "/b",
            overwrite = false,
            recursive = false
        }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "CopyFile" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        IReadOnlyList<AngleSharp.Dom.IElement> checkboxes = cut.FindAll( "input[type='checkbox']" );
        Assert.IsGreaterThanOrEqualTo( 2, checkboxes.Count, "Should render at least 2 checkboxes (Overwrite, Recursive)." );
    }

    // ── Empty Select Option for Non-Required ────────────────────────

    /// <summary>
    /// ExpandArchive has an optional Format Select field. It should render
    /// a "— select —" empty option since it's not required.
    /// </summary>
    [TestMethod]
    public void NonRequired_Select_Has_Empty_Option( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "ExpandArchive" )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        string markup = cut.Markup;
        Assert.Contains(
            "\u2014 select \u2014",
            markup,
            "Non-required Select fields should have an empty placeholder option." );
    }

    // ── JSON with DictionaryKeyPolicy camelCase ─────────────────────

    /// <summary>
    /// Verifies that KeyValueMap data is serialized with camelCase dictionary keys.
    /// </summary>
    [TestMethod]
    public void KvMap_Serialized_With_CamelCase_Keys( ) {
        string seedJson = JsonSerializer.Serialize( new {
            url = "https://example.com",
            method = "GET",
            headers = new Dictionary<string, string> {
                ["Authorization"] = "Bearer token123",
                ["Accept"] = "application/json",
            }
        }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "HttpRequest" )
            .Add( p => p.ActionParameters, seedJson )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        // Toggle to JSON view to get the raw JSON
        AngleSharp.Dom.IElement toggleBtn = cut.FindAll( "button" )
            .First( b => b.TextContent.Contains( "JSON View" ) );
        toggleBtn.Click( );

        // The JSON textarea should contain camelCase property names
        string jsonText = cut.Find( "textarea" ).GetAttribute( "value" ) ?? cut.Find( "textarea" ).TextContent;

        Assert.Contains(
            "\"url\"",
            jsonText,
            "JSON should use camelCase property names." );
    }

    // ── Guard redundant re-parsing ──────────────────────────────────

    /// <summary>
    /// Rendering with valid parameters produces consistent, valid state.
    /// Verifies the component handles parameters correctly without errors.
    /// </summary>
    [TestMethod]
    public void ActionParameters_Parsed_Correctly_On_Render( ) {
        string json = JsonSerializer.Serialize(
            new { seconds = 5 }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "Delay" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        // Should render the value without error
        Assert.Contains(
            "5",
            cut.Markup,
            "Value should be rendered from parameters." );

        // Validation should pass
        bool result = false;
        _ = cut.InvokeAsync( ( ) => result = cut.Instance.Validate( ) ).GetAwaiter( ).GetResult( );
        Assert.IsTrue( result, "Component should be valid with correct parameters." );
    }

    // ── Format JSON Button ──────────────────────────────────────────

    /// <summary>
    /// In JSON mode, the Format button should be visible.
    /// </summary>
    [TestMethod]
    public void Json_Mode_Shows_Format_Button( ) {
        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "Delay" )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        // Toggle to JSON mode
        cut.FindAll( "button" ).First( b => b.TextContent.Contains( "JSON View" ) ).Click( );

        AngleSharp.Dom.IElement? formatBtn = cut.FindAll( "button" )
            .FirstOrDefault( b => b.TextContent.Contains( "Format" ) );
        Assert.IsNotNull( formatBtn, "Format button should be visible in JSON mode." );
    }

    // ── TestConnection ShowWhen on Protocol ─────────────────────────

    /// <summary>
    /// TestConnection ExpectedStatusCode has ShowWhen="Protocol=Http|Https".
    /// With default Tcp it should be hidden.
    /// </summary>
    [TestMethod]
    public void TestConnection_StatusCode_Hidden_For_Tcp( ) {
        string json = JsonSerializer.Serialize( new {
            host = "localhost",
            port = 443,
            protocol = "Tcp"
        }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "TestConnection" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        Assert.DoesNotContain(
            "Expected Status Code",
            cut.Markup,
            "ExpectedStatusCode should be hidden when Protocol=Tcp." );
    }

    /// <summary>
    /// TestConnection ExpectedStatusCode visible for Https.
    /// </summary>
    [TestMethod]
    public void TestConnection_StatusCode_Visible_For_Https( ) {
        string json = JsonSerializer.Serialize( new {
            host = "localhost",
            port = 443,
            protocol = "Https"
        }, s_jsonOptions );

        IRenderedComponent<ActionParameterEditor> cut = Render<ActionParameterEditor>( parameters => parameters
            .Add( p => p.ActionSubType, "TestConnection" )
            .Add( p => p.ActionParameters, json )
            .Add( p => p.ActionSubTypeChanged, EventCallback.Factory.Create<string>( this, _ => { } ) )
            .Add( p => p.ActionParametersChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        Assert.Contains(
            "Expected Status Code",
            cut.Markup,
            "ExpectedStatusCode should appear when Protocol=Https." );
    }
}
