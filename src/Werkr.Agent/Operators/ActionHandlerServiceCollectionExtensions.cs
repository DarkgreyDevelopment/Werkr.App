using Werkr.Core.Operators;

namespace Werkr.Agent.Operators;

/// <summary>
/// Service collection extensions for registering built-in action handlers.
/// </summary>
public static class ActionHandlerServiceCollectionExtensions {
    /// <summary>
    /// Registers all <see cref="IActionHandler"/> implementations from the
    /// <c>Werkr.Agent</c> assembly as singleton services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddActionHandlers( this IServiceCollection services ) {
        Type actionHandlerType = typeof( IActionHandler );
        IEnumerable<Type> handlerTypes = typeof( ActionHandlerServiceCollectionExtensions )
            .Assembly
            .GetTypes( )
            .Where( t => actionHandlerType.IsAssignableFrom( t )
                && !t.IsAbstract
                && !t.IsInterface );

        foreach (Type handlerType in handlerTypes) {
            _ = services.AddSingleton( actionHandlerType, handlerType );
        }

        return services;
    }
}
