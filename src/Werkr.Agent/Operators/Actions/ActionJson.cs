using System.Text.Json;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Shared JSON serializer options for action handler parameter deserialization.
/// </summary>
internal static class ActionJson {
    internal static readonly JsonSerializerOptions SerializerOptions = new( ) {
        PropertyNameCaseInsensitive = true,
    };
}
