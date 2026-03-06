using System.Text.Json;

namespace Werkr.Common.Models.Actions;

/// <summary>
/// Describes a built-in action to execute. A pure data record - serialization
/// options are handled at the handler layer via <c>ActionJson.SerializerOptions</c>
/// (case-insensitive on deserialization at consumption). Serialization at creation
/// uses <see cref="JsonSerializer.SerializeToElement{T}(T, JsonSerializerOptions?)"/>
/// with camelCase policy at the call site. This record is intentionally a pure
/// data carrier.
/// </summary>
public sealed record ActionDescriptor {

    /// <summary>The action name string (e.g. "CopyFile", "StartProcess").</summary>
    public required string Action { get; init; }

    /// <summary>JSON parameters matching the action's parameter record shape.</summary>
    public required JsonElement Parameters { get; init; }
}
