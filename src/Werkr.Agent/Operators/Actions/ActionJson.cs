using System.Text.Json;
using Werkr.Core.Operators;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Shared JSON serializer options for action handler parameter deserialization.
/// </summary>
internal static class ActionJson {
    /// <summary>
    /// Shared <see cref="JsonSerializerOptions"/> instance configured with case-insensitive
    /// property name matching. Used by all <see cref="IActionHandler"/> implementations to deserialize
    /// their strongly-typed parameter objects from the raw JSON payload.
    /// </summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new( ) {
        PropertyNameCaseInsensitive = true,
    };
}
