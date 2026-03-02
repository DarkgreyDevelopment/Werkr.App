using System.Text.Json;

using Werkr.Common.Models.Actions;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Helper for building <see cref="ActionDescriptor"/> instances in tests
/// with type-safe parameter serialization.
/// </summary>
internal static class TestActionDescriptor {

    private static readonly JsonSerializerOptions s_options = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Serializes an object to a <see cref="JsonElement"/> using camelCase naming.
    /// Shared by all handler tests to avoid creating per-call serializer options.
    /// </summary>
    public static JsonElement Serialize<T>( T value ) =>
        JsonSerializer.SerializeToElement( value, s_options );

    /// <summary>
    /// Creates an <see cref="ActionDescriptor"/> with serialized parameters.
    /// </summary>
    public static ActionDescriptor Create<T>( string action, T parameters ) =>
        new( ) {
            Action = action,
            Parameters = Serialize( parameters ),
        };

    /// <summary>
    /// Creates an <see cref="ActionDescriptor"/> with an empty JSON object parameter.
    /// </summary>
    public static ActionDescriptor Create( string action ) =>
        new( ) {
            Action = action,
            Parameters = JsonSerializer.SerializeToElement( new { } ),
        };
}
