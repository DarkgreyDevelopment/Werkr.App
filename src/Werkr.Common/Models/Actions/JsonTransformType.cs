namespace Werkr.Common.Models.Actions;

/// <summary>
/// The type of JSON transformation operation to apply.
/// Used by <see cref="JsonTransformOperation"/> within the <c>TransformJson</c> action.
/// </summary>
public enum JsonTransformType {

    /// <summary>
    /// Extracts the value at a JSON Pointer path.
    /// The extracted value replaces the entire document as output.
    /// </summary>
    Extract,

    /// <summary>
    /// Sets or replaces the value at a JSON Pointer path.
    /// Creates intermediate objects if needed.
    /// </summary>
    Set,

    /// <summary>
    /// Removes the property or element at a JSON Pointer path.
    /// Succeeds silently if the path does not exist.
    /// </summary>
    Delete,

    /// <summary>
    /// Deep-merges a JSON object into the object at a JSON Pointer path.
    /// Fails if the target is not an object.
    /// </summary>
    Merge,
}
