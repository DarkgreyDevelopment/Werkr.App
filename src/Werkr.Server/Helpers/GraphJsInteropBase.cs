using Microsoft.JSInterop;

namespace Werkr.Server.Helpers;

/// <summary>
/// Base class for X6 graph JS interop wrappers.
/// Manages <see cref="IJSObjectReference"/> module lifecycle and <see cref="DotNetObjectReference{T}"/>.
/// </summary>
public abstract class GraphJsInteropBase<TSelf> : IAsyncDisposable where TSelf : GraphJsInteropBase<TSelf> {

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private DotNetObjectReference<TSelf>? _dotNetRef;

    /// <summary>Gets the loaded JS module reference (null until init is called).</summary>
    protected IJSObjectReference? Module => _module;

    /// <summary>Gets the .NET object reference for JS→.NET callbacks.</summary>
    protected DotNetObjectReference<TSelf>? DotNetRef => _dotNetRef;

    /// <summary>The JS module path to import (e.g. "/js/dist/dag-readonly.js").</summary>
    protected abstract string ModulePath { get; }

    /// <summary>The JS function name to call when destroying the graph.</summary>
    protected abstract string DestroyFunctionName { get; }

    /// <summary>Creates a new interop wrapper using the specified JS runtime.</summary>
    protected GraphJsInteropBase( IJSRuntime js ) {
        _js = js;
    }

    /// <summary>Import the JS module and create the DotNetObjectReference.</summary>
    protected async Task LoadModuleAsync( ) {
        _module = await _js.InvokeAsync<IJSObjectReference>( "import", ModulePath );
        _dotNetRef = DotNetObjectReference.Create( (TSelf) this );
    }

    /// <summary>Invoke a void JS function on the loaded module.</summary>
    protected async Task InvokeVoidAsync( string identifier, params object?[] args ) {
        if (_module is null) return;
        await _module.InvokeVoidAsync( identifier, args );
    }

    /// <summary>Invoke a JS function on the loaded module and return a result.</summary>
    protected async Task<T> InvokeAsync<T>( string identifier, params object?[] args ) {
        if (_module is null) throw new InvalidOperationException( "Module not loaded." );
        return await _module.InvokeAsync<T>( identifier, args );
    }

    /// <summary>Destroy the graph and clean up JS resources.</summary>
    public async Task DestroyAsync( ) {
        await InvokeVoidAsync( DestroyFunctionName );
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync( ) {
        if (_module is not null) {
            try {
                await DestroyAsync( );
                await _module.DisposeAsync( );
            } catch (JSDisconnectedException) {
                // Circuit disconnected — JS cleanup not possible, safe to ignore
            }
        }
        _dotNetRef?.Dispose( );
        GC.SuppressFinalize( this );
    }
}
