using Microsoft.JSInterop;

namespace Werkr.Server.Helpers;

/// <summary>
/// Base class for X6 graph JS interop wrappers.
/// Manages <see cref="IJSObjectReference"/> module lifecycle and <see cref="DotNetObjectReference{T}"/>.
/// </summary>
/// <remarks>Creates a new interop wrapper using the specified JS runtime.</remarks>
public abstract class GraphJsInteropBase<TSelf>( IJSRuntime js ) : IAsyncDisposable where TSelf : GraphJsInteropBase<TSelf> {

    /// <summary>Gets the loaded JS module reference (null until init is called).</summary>
    protected IJSObjectReference? Module { get; private set; }

    /// <summary>Gets the .NET object reference for JS→.NET callbacks.</summary>
    protected DotNetObjectReference<TSelf>? DotNetRef { get; private set; }

    /// <summary>The JS module path to import (e.g. "/js/dist/dag-readonly.js").</summary>
    protected abstract string ModulePath { get; }

    /// <summary>The JS function name to call when destroying the graph.</summary>
    protected abstract string DestroyFunctionName { get; }

    /// <summary>Import the JS module and create the DotNetObjectReference.</summary>
    protected async Task LoadModuleAsync( ) {
        Module = await js.InvokeAsync<IJSObjectReference>( "import", ModulePath );
        DotNetRef = DotNetObjectReference.Create( (TSelf)this );
    }

    /// <summary>Invoke a void JS function on the loaded module.</summary>
    protected async Task InvokeVoidAsync( string identifier, params object?[] args ) {
        if (Module is null) {
            return;
        }

        await Module.InvokeVoidAsync( identifier, args );
    }

    /// <summary>Invoke a JS function on the loaded module and return a result.</summary>
    protected async Task<T> InvokeAsync<T>( string identifier, params object?[] args ) {
        return Module is null
            ? throw new InvalidOperationException( "Module not loaded." )
            : await Module.InvokeAsync<T>( identifier, args );
    }

    /// <summary>Destroy the graph and clean up JS resources.</summary>
    public async Task DestroyAsync( ) {
        await InvokeVoidAsync( DestroyFunctionName );
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync( ) {
        if (Module is not null) {
            try {
                await DestroyAsync( );
                await Module.DisposeAsync( );
            } catch (JSDisconnectedException) {
                // Circuit disconnected — JS cleanup not possible, safe to ignore
            }
        }
        DotNetRef?.Dispose( );
        GC.SuppressFinalize( this );
    }
}
