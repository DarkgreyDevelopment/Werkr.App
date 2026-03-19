using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Werkr.Common.Models;
using Werkr.Server.Components.Shared;

namespace Werkr.Tests.Server.Components;

/// <summary>
/// bUnit tests for the <see cref="TaskSetupModal"/> component. Validates modal
/// visibility, form rendering, successful submission, cancellation, and edit mode.
/// </summary>
[TestClass]
public class TaskSetupModalTests : BunitContext {

    /// <summary>
    /// Registers a fake <see cref="IHttpClientFactory"/> that returns an <see cref="HttpClient"/>
    /// backed by the given handler.
    /// </summary>
    private void RegisterHttpClient( HttpMessageHandler handler ) {
        HttpClient client = new( handler ) { BaseAddress = new Uri( "http://localhost" ) };
        IHttpClientFactory factory = new FakeHttpClientFactory( client );
        _ = Services.AddSingleton( factory );
    }

    /// <summary>
    /// Verifies that the modal is hidden by default (no modal markup rendered).
    /// </summary>
    [TestMethod]
    public void Modal_IsHidden_ByDefault( ) {
        RegisterHttpClient( new StubHandler( HttpStatusCode.OK, "{}" ) );

        IRenderedComponent<TaskSetupModal> cut = Render<TaskSetupModal>( parameters =>
            parameters.Add( p => p.WorkflowId, 42L )
                      .Add( p => p.OnTaskCreated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) ) );

        // Modal should not be visible
        _ = Assert.ThrowsExactly<ElementNotFoundException>( ( ) => cut.Find( ".modal" ) );
    }

    /// <summary>
    /// Verifies that calling <c>Show()</c> makes the modal visible with the expected form fields.
    /// </summary>
    [TestMethod]
    public void Show_RendersModalWithFormFields( ) {
        RegisterHttpClient( new StubHandler( HttpStatusCode.OK, "{}" ) );

        IRenderedComponent<TaskSetupModal> cut = Render<TaskSetupModal>( parameters =>
            parameters.Add( p => p.WorkflowId, 42L )
                      .Add( p => p.OnTaskCreated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) ) );

        _ = cut.InvokeAsync( ( ) => cut.Instance.Show( ) );

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

        IRenderedComponent<TaskSetupModal> cut = Render<TaskSetupModal>( parameters =>
            parameters.Add( p => p.WorkflowId, 42L )
                      .Add( p => p.OnTaskCreated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) ) );

        _ = cut.InvokeAsync( ( ) => cut.Instance.Show( ) );

        // Click Cancel
        AngleSharp.Dom.IElement cancelButton = cut.Find( "button.btn-outline-secondary" );
        cancelButton.Click( );

        // Modal should be hidden again
        _ = Assert.ThrowsExactly<ElementNotFoundException>( ( ) => cut.Find( ".modal" ) );
    }

    /// <summary>
    /// Verifies that the Action Type dropdown contains the expected options.
    /// </summary>
    [TestMethod]
    public void ActionTypeDropdown_HasExpectedOptions( ) {
        RegisterHttpClient( new StubHandler( HttpStatusCode.OK, "{}" ) );

        IRenderedComponent<TaskSetupModal> cut = Render<TaskSetupModal>( parameters =>
            parameters.Add( p => p.WorkflowId, 42L )
                      .Add( p => p.OnTaskCreated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) ) );

        _ = cut.InvokeAsync( ( ) => cut.Instance.Show( ) );

        IReadOnlyList<AngleSharp.Dom.IElement> options = cut.FindAll( ".form-select option" );
        string[] optionValues = [.. options.Select( o => o.GetAttribute( "value" ) ?? "" )];

        Assert.Contains( "PowerShellCommand", optionValues, "Should have PowerShellCommand." );
        Assert.Contains( "ShellCommand", optionValues, "Should have ShellCommand." );
        Assert.Contains( "ShellScript", optionValues, "Should have ShellScript." );
        Assert.Contains( "Action", optionValues, "Should have Action." );
    }

    /// <summary>
    /// Verifies that calling <c>Show(TaskDto)</c> populates the form for editing with the correct title and button text.
    /// </summary>
    [TestMethod]
    public void Show_WithTaskDto_PopulatesFormForEditing( ) {
        RegisterHttpClient( new StubHandler( HttpStatusCode.OK, "{}" ) );

        TaskDto existingTask = new(
            Id: 7,
            Name: "My Task",
            Description: "Test desc",
            ActionType: "ShellCommand",
            Content: "echo hello",
            Arguments: ["--verbose"],
            TargetTags: ["linux"],
            Enabled: true,
            TimeoutMinutes: 60,
            SyncIntervalMinutes: 5,
            SuccessCriteria: null,
            EffectiveSuccessCriteria: "ExitCodeZero",
            WorkflowId: 42 );

        IRenderedComponent<TaskSetupModal> cut = Render<TaskSetupModal>( parameters =>
            parameters.Add( p => p.WorkflowId, 42L )
                      .Add( p => p.OnTaskCreated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) )
                      .Add( p => p.OnTaskUpdated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) ) );

        _ = cut.InvokeAsync( ( ) => cut.Instance.Show( existingTask ) );

        // Modal should be visible with edit title
        Assert.Contains( "Edit Task", cut.Markup, "Should render edit mode title." );

        // Should have Save Changes button instead of Create Task
        Assert.Contains( "Save Changes", cut.Markup, "Should render save button in edit mode." );

        // Should NOT have Create Task button
        Assert.DoesNotContain( "Create Task", cut.Markup, "Should not render create button in edit mode." );
    }

    /// <summary>
    /// Verifies that submitting in edit mode sends a PUT request to the correct endpoint.
    /// </summary>
    [TestMethod]
    public void Submit_InEditMode_SendsPutRequest( ) {
        HttpMethod? capturedMethod = null;
        string? capturedUri = null;

        CapturingHandler handler = new( HttpStatusCode.OK,
            JsonSerializer.Serialize( new TaskDto(
                Id: 7, Name: "Updated", Description: "Updated desc",
                ActionType: "ShellCommand", Content: "echo updated",
                Arguments: null, TargetTags: ["linux"], Enabled: true,
                TimeoutMinutes: 60, SyncIntervalMinutes: 5,
                SuccessCriteria: null, EffectiveSuccessCriteria: "ExitCodeZero",
                WorkflowId: 42 ) ),
            ( method, uri ) => { capturedMethod = method; capturedUri = uri; } );

        RegisterHttpClient( handler );

        TaskDto existingTask = new(
            Id: 7, Name: "My Task", Description: "Test desc",
            ActionType: "ShellCommand", Content: "echo hello",
            Arguments: null, TargetTags: ["linux"], Enabled: true,
            TimeoutMinutes: 60, SyncIntervalMinutes: 5,
            SuccessCriteria: null, EffectiveSuccessCriteria: "ExitCodeZero",
            WorkflowId: 42 );

        IRenderedComponent<TaskSetupModal> cut = Render<TaskSetupModal>( parameters =>
            parameters.Add( p => p.WorkflowId, 42L )
                      .Add( p => p.OnTaskCreated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) )
                      .Add( p => p.OnTaskUpdated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) ) );

        _ = cut.InvokeAsync( ( ) => cut.Instance.Show( existingTask ) );

        // Submit the form
        cut.Find( "form" ).Submit( );

        Assert.AreEqual( HttpMethod.Put, capturedMethod, "Should send PUT request in edit mode." );
        Assert.AreEqual( "/api/tasks/7", capturedUri, "Should target the correct task endpoint." );
    }

    /// <summary>
    /// Verifies that a successful edit invokes the OnTaskUpdated callback.
    /// </summary>
    [TestMethod]
    public void Submit_InEditMode_InvokesOnTaskUpdated( ) {
        TaskDto? updatedResult = null;

        TaskDto responseDto = new(
            Id: 7, Name: "Updated Task", Description: "Updated desc",
            ActionType: "ShellCommand", Content: "echo updated",
            Arguments: null, TargetTags: ["linux"], Enabled: true,
            TimeoutMinutes: 60, SyncIntervalMinutes: 5,
            SuccessCriteria: null, EffectiveSuccessCriteria: "ExitCodeZero",
            WorkflowId: 42 );

        RegisterHttpClient( new StubHandler( HttpStatusCode.OK, JsonSerializer.Serialize( responseDto ) ) );

        TaskDto existingTask = new(
            Id: 7, Name: "My Task", Description: "Test desc",
            ActionType: "ShellCommand", Content: "echo hello",
            Arguments: null, TargetTags: ["linux"], Enabled: true,
            TimeoutMinutes: 60, SyncIntervalMinutes: 5,
            SuccessCriteria: null, EffectiveSuccessCriteria: "ExitCodeZero",
            WorkflowId: 42 );

        IRenderedComponent<TaskSetupModal> cut = Render<TaskSetupModal>( parameters =>
            parameters.Add( p => p.WorkflowId, 42L )
                      .Add( p => p.OnTaskCreated, EventCallback.Factory.Create<TaskDto>( this, _ => { } ) )
                      .Add( p => p.OnTaskUpdated, EventCallback.Factory.Create<TaskDto>( this, t => updatedResult = t ) ) );

        _ = cut.InvokeAsync( ( ) => cut.Instance.Show( existingTask ) );

        // Submit the form
        cut.Find( "form" ).Submit( );

        Assert.IsNotNull( updatedResult, "OnTaskUpdated callback should have been invoked." );
        Assert.AreEqual( "Updated Task", updatedResult!.Name, "Should receive the updated task DTO." );
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

    /// <summary>Captures the HTTP method and URI before returning the configured response.</summary>
    private sealed class CapturingHandler(
        HttpStatusCode statusCode,
        string content,
        Action<HttpMethod, string?> onSend ) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken ) {
            onSend( request.Method, request.RequestUri?.AbsolutePath );
            return Task.FromResult( new HttpResponseMessage( statusCode ) {
                Content = new StringContent( content, System.Text.Encoding.UTF8, "application/json" ),
            } );
        }
    }

    /// <summary>Fake <see cref="IHttpClientFactory"/> that always returns the same client.</summary>
    private sealed class FakeHttpClientFactory( HttpClient client ) : IHttpClientFactory {
        public HttpClient CreateClient( string name ) => client;
    }
}
