using Bunit;
using Microsoft.AspNetCore.Components;
using Werkr.Server.Components.Shared;

namespace Werkr.Tests.Server.Components;

/// <summary>
/// bUnit tests for the <see cref="ConditionBuilder"/> component. Validates structured mode
/// expression generation, operator/type switching, advanced/raw toggle, and expression validation.
/// </summary>
[TestClass]
public class ConditionBuilderTests : BunitContext {

    /// <summary>
    /// Renders the component with no initial expression and verifies the structured
    /// mode is shown by default with the ExitCode type selected.
    /// </summary>
    [TestMethod]
    public void Renders_StructuredMode_ByDefault( ) {
        IRenderedComponent<ConditionBuilder> cut = Render<ConditionBuilder>( parameters =>
            parameters.Add( p => p.Expression, null )
                      .Add( p => p.ExpressionChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        // Should not show the raw input
        _ = Assert.ThrowsExactly<ElementNotFoundException>( ( ) => cut.Find( "input[type=text]" ) );

        // Should show the structured dropdowns — Type select should exist
        IReadOnlyList<AngleSharp.Dom.IElement> selects = cut.FindAll( "select" );
        Assert.IsGreaterThanOrEqualTo( 1, selects.Count, "Should render at least the Type dropdown." );
    }

    /// <summary>
    /// Applies a structured exit code expression and verifies the expression callback fires
    /// with the correct value.
    /// </summary>
    [TestMethod]
    public void Apply_ExitCodeEquals_GeneratesCorrectExpression( ) {
        string? captured = null;
        IRenderedComponent<ConditionBuilder> cut = Render<ConditionBuilder>( parameters =>
            parameters.Add( p => p.Expression, null )
                      .Add( p => p.ExpressionChanged, EventCallback.Factory.Create<string?>( this, v => captured = v ) ) );

        // The defaults are ExitCode, ==, 0 — just click Apply
        AngleSharp.Dom.IElement applyButton = cut.Find( "button.btn-outline-primary" );
        applyButton.Click( );

        Assert.AreEqual( "$exitCode == 0", captured );
    }

    /// <summary>
    /// Verifies that changing the operator dropdown changes the generated expression.
    /// </summary>
    [TestMethod]
    public void Apply_ExitCodeNotEqual_GeneratesCorrectExpression( ) {
        string? captured = null;
        IRenderedComponent<ConditionBuilder> cut = Render<ConditionBuilder>( parameters =>
            parameters.Add( p => p.Expression, null )
                      .Add( p => p.ExpressionChanged, EventCallback.Factory.Create<string?>( this, v => captured = v ) ) );

        // Change operator to !=
        IReadOnlyList<AngleSharp.Dom.IElement> selects = cut.FindAll( "select" );
        // Second select is the operator dropdown (first is Type)
        AngleSharp.Dom.IElement operatorSelect = selects[1];
        operatorSelect.Change( "!=" );

        cut.Find( "button.btn-outline-primary" ).Click( );

        Assert.AreEqual( "$exitCode != 0", captured );
    }

    /// <summary>
    /// Verifies that selecting SuccessStatus type and applying generates the correct expression.
    /// </summary>
    [TestMethod]
    public void Apply_SuccessTrue_GeneratesCorrectExpression( ) {
        string? captured = null;
        IRenderedComponent<ConditionBuilder> cut = Render<ConditionBuilder>( parameters =>
            parameters.Add( p => p.Expression, null )
                      .Add( p => p.ExpressionChanged, EventCallback.Factory.Create<string?>( this, v => captured = v ) ) );

        // Change type to SuccessStatus
        AngleSharp.Dom.IElement typeSelect = cut.FindAll( "select" )[0];
        typeSelect.Change( "SuccessStatus" );

        // Default success value is "true" — apply
        cut.Find( "button.btn-outline-primary" ).Click( );

        Assert.AreEqual( "$? -eq $true", captured );
    }

    /// <summary>
    /// Verifies switching to advanced mode reveals the raw text input.
    /// </summary>
    [TestMethod]
    public void SwitchToAdvanced_ShowsRawInput( ) {
        IRenderedComponent<ConditionBuilder> cut = Render<ConditionBuilder>( parameters =>
            parameters.Add( p => p.Expression, null )
                      .Add( p => p.ExpressionChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        // Click "Advanced / Raw" button
        AngleSharp.Dom.IElement advancedButton = cut.Find( "button.btn-outline-secondary" );
        advancedButton.Click( );

        // Now the raw text input should be present
        AngleSharp.Dom.IElement rawInput = cut.Find( "input[type=text]" );
        Assert.IsNotNull( rawInput );
    }

    /// <summary>
    /// Verifies that a valid raw expression is accepted and fires the callback.
    /// </summary>
    [TestMethod]
    public void ApplyRaw_ValidExpression_FiresCallback( ) {
        string? captured = null;
        IRenderedComponent<ConditionBuilder> cut = Render<ConditionBuilder>( parameters =>
            parameters.Add( p => p.Expression, null )
                      .Add( p => p.ExpressionChanged, EventCallback.Factory.Create<string?>( this, v => captured = v ) ) );

        // Switch to advanced mode
        cut.Find( "button.btn-outline-secondary" ).Click( );

        // Type a valid expression
        cut.Find( "input[type=text]" ).Input( "$exitCode >= 1" );

        // Click Apply
        IReadOnlyList<AngleSharp.Dom.IElement> buttons = cut.FindAll( "button.btn-outline-primary" );
        AngleSharp.Dom.IElement applyButton = buttons[^1]; // last outline-primary is the Apply button
        applyButton.Click( );

        Assert.AreEqual( "$exitCode >= 1", captured );
    }

    /// <summary>
    /// Verifies that a custom raw expression is accepted and fires the callback
    /// (raw mode does not restrict to known patterns).
    /// </summary>
    [TestMethod]
    public void ApplyRaw_CustomExpression_FiresCallback( ) {
        string? captured = null;
        IRenderedComponent<ConditionBuilder> cut = Render<ConditionBuilder>( parameters =>
            parameters.Add( p => p.Expression, null )
                      .Add( p => p.ExpressionChanged, EventCallback.Factory.Create<string?>( this, v => captured = v ) ) );

        // Switch to advanced mode
        cut.Find( "button.btn-outline-secondary" ).Click( );

        // Type a custom expression not matching known patterns
        cut.Find( "input[type=text]" ).Input( "custom_var > 42" );

        // Click Apply
        IReadOnlyList<AngleSharp.Dom.IElement> buttons = cut.FindAll( "button.btn-outline-primary" );
        buttons[^1].Click( );

        // Callback should have been fired with the custom expression
        Assert.AreEqual( "custom_var > 42", captured );
    }

    /// <summary>
    /// Verifies that an existing expression is parsed into the structured UI on initial render.
    /// </summary>
    [TestMethod]
    public void ExistingExpression_ParsedIntoStructuredUI( ) {
        IRenderedComponent<ConditionBuilder> cut = Render<ConditionBuilder>( parameters =>
            parameters.Add( p => p.Expression, "$? -eq $false" )
                      .Add( p => p.ExpressionChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        // Should show the expression badge
        AngleSharp.Dom.IElement badge = cut.Find( ".badge" );
        Assert.Contains( "$? -eq $false", badge.TextContent );
    }

    /// <summary>
    /// Verifies the documentation section with supported patterns is rendered.
    /// </summary>
    [TestMethod]
    public void SupportedPatterns_Documentation_IsRendered( ) {
        IRenderedComponent<ConditionBuilder> cut = Render<ConditionBuilder>( parameters =>
            parameters.Add( p => p.Expression, null )
                      .Add( p => p.ExpressionChanged, EventCallback.Factory.Create<string?>( this, _ => { } ) ) );

        // Should have a <details> element with pattern documentation
        AngleSharp.Dom.IElement details = cut.Find( "details" );
        Assert.IsNotNull( details );
        Assert.Contains( "$? -eq $true", details.TextContent );
    }
}
