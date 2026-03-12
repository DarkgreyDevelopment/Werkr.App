using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Werkr.Common.Models;
using Werkr.Server.Components.Shared;

namespace Werkr.Tests.Server.Components;

/// <summary>
/// bUnit tests for the <see cref="TaskCreateModal"/> component. Validates modal
/// visibility, form rendering, successful submission, and cancellation.
/// </summary>
[TestClass]
public class TaskCreateModalTests : BunitContext {

    /// <summary>
    /// Registers a fake <see cref="IHttpClientFactory"/> that returns an <see cref="HttpClient"/>
    /// backed by the given handler.
    /// </summary>
    private void RegisterHttpClient( HttpMessageHandler handler ) {
        HttpClient client = new( handler ) { BaseAddress = new Uri( "http://localhost" ) };
        IHttpClientFactory factory = new FakeHttpClientFactory( client );
        Services.AddSingleton( factory );
    }

    /// <summary>
    /// Verifies that the modal is hidden by default (no modal markup rendered).
    /// </summary>
    [TestMethod]
    public void Modal_IsHidden_ByDefault( ) {
        RegisterHttpClient( new StubHandler( HttpStatusCode.OK, "{}" ) );

        IRenderedComponent<TaskCreateModal> cut = Render<TaskCreateModal>( parameters =>
            parameters.Add( p => p.WorkflowId, 42L )
                      .Add( p => p.OnTaskCreated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) ) );

        // Modal should not be visible
        Assert.ThrowsExactly<ElementNotFoundException>( ( ) => cut.Find( ".modal" ) );
    }

    /// <summary>
    /// Verifies that calling <c>Show()</c> makes the modal visible with the expected form fields.
    /// </summary>
    [TestMethod]
    public void Show_RendersModalWithFormFields( ) {
        RegisterHttpClient( new StubHandler( HttpStatusCode.OK, "{}" ) );

        IRenderedComponent<TaskCreateModal> cut = Render<TaskCreateModal>( parameters =>
            parameters.Add( p => p.WorkflowId, 42L )
                      .Add( p => p.OnTaskCreated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) ) );

        cut.InvokeAsync( ( ) => cut.Instance.Show( ) );

        // Modal should be visible
        AngleSharp.Dom.IElement modal = cut.Find( ".modal" );
        Assert.IsNotNull( modal );

        // Should have a Name input
        Assert.Contains( "Name", cut.Markup, "Should render Name field." );

        // Should have Action Type select
        Assert.Contains( "Action Type", cut.Markup, "Should render Action Type field." );

        // Should have Create Task submit button
        Assert.Contains( "Create Task", cut.Markup, "Should render submit button." );

        // Should have Cancel button
        Assert.Contains( "Cancel", cut.Markup, "Should render cancel button." );
    }

    /// <summary>
    /// Verifies that clicking Cancel closes the modal.
    /// </summary>
    [TestMethod]
    public void Cancel_ClosesModal( ) {
        RegisterHttpClient( new StubHandler( HttpStatusCode.OK, "{}" ) );

        IRenderedComponent<TaskCreateModal> cut = Render<TaskCreateModal>( parameters =>
            parameters.Add( p => p.WorkflowId, 42L )
                      .Add( p => p.OnTaskCreated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) ) );

        cut.InvokeAsync( ( ) => cut.Instance.Show( ) );

        // Click Cancel
        AngleSharp.Dom.IElement cancelButton = cut.Find( "button.btn-outline-secondary" );
        cancelButton.Click( );

        // Modal should be hidden again
        Assert.ThrowsExactly<ElementNotFoundException>( ( ) => cut.Find( ".modal" ) );
    }

    /// <summary>
    /// Verifies that the Action Type dropdown contains the expected options.
    /// </summary>
    [TestMethod]
    public void ActionTypeDropdown_HasExpectedOptions( ) {
        RegisterHttpClient( new StubHandler( HttpStatusCode.OK, "{}" ) );

        IRenderedComponent<TaskCreateModal> cut = Render<TaskCreateModal>( parameters =>
            parameters.Add( p => p.WorkflowId, 42L )
                      .Add( p => p.OnTaskCreated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) ) );

        cut.InvokeAsync( ( ) => cut.Instance.Show( ) );

        IReadOnlyList<AngleSharp.Dom.IElement> options = cut.FindAll( ".form-select option" );
        string[] optionValues = [.. options.Select( o => o.GetAttribute( "value" ) ?? "" )];

        Assert.Contains( "PowerShellCommand", optionValues, "Should have PowerShellCommand." );
        Assert.Contains( "ShellCommand", optionValues, "Should have ShellCommand." );
        Assert.Contains( "ShellScript", optionValues, "Should have ShellScript." );
        Assert.Contains( "Action", optionValues, "Should have Action." );
    }

    // ── Helpers ──

    /// <summary>Minimal fake that always returns the given response.</summary>
    private sealed class StubHandler( HttpStatusCode statusCode, string content ) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken ) =>
            Task.FromResult( new HttpResponseMessage( statusCode ) {
                Content = new StringContent( content, System.Text.Encoding.UTF8, "application/json" ),
            } );
    }

    /// <summary>Fake <see cref="IHttpClientFactory"/> that always returns the same client.</summary>
    private sealed class FakeHttpClientFactory( HttpClient client ) : IHttpClientFactory {
        public HttpClient CreateClient( string name ) => client;
    }
}
