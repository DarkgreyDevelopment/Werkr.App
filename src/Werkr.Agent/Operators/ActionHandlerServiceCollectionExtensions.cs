using System.Reflection;
using Werkr.Common.Attributes;
using Werkr.Common.Models.Actions;
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

    /// <summary>
    /// Validates that every <see cref="IActionHandler"/> implementation with a matching
    /// <see cref="ActionRegistry"/> descriptor has an <see cref="ActionCategoryAttribute"/>
    /// whose value matches the descriptor's <see cref="ActionFormDescriptor.Category"/>.
    /// Logs a warning for each mismatch.
    /// </summary>
    /// <param name="serviceProvider">The built service provider.</param>
    public static void ValidateActionCategoryAttributes( IServiceProvider serviceProvider ) {
        ILogger<IActionHandler> logger = serviceProvider.GetRequiredService<ILogger<IActionHandler>>( );
        IEnumerable<IActionHandler> handlers = serviceProvider.GetServices<IActionHandler>( );

        foreach (IActionHandler handler in handlers) {
            Type handlerType = handler.GetType( );
            ActionCategoryAttribute? attribute = handlerType.GetCustomAttribute<ActionCategoryAttribute>( );

            if (!ActionRegistry.Actions.TryGetValue( handler.Action, out ActionFormDescriptor? descriptor )) {
                continue;
            }

            if (attribute is null) {
                logger.LogWarning(
                    "Action handler {HandlerType} for action '{Action}' is missing [ActionCategory] attribute.",
                    handlerType.Name, handler.Action );
                continue;
            }

            if (!string.Equals( attribute.Category, descriptor.Category, StringComparison.Ordinal )) {
                logger.LogWarning(
                    "Action handler {HandlerType} has [ActionCategory(\"{AttributeCategory}\")] but registry descriptor has Category=\"{RegistryCategory}\".",
                    handlerType.Name, attribute.Category, descriptor.Category );
            }
        }
    }
}
