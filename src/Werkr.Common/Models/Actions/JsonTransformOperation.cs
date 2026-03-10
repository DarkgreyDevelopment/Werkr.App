namespace Werkr.Common.Models.Actions;

/// <summary>
/// A single JSON transformation operation applied within the <c>TransformJson</c> action.
/// Operations are applied in array order — each receives the output of the previous operation.
/// </summary>
public sealed record JsonTransformOperation {

    /// <summary>
    /// The type of transformation to perform.
    /// </summary>
    public required JsonTransformType Type { get; init; }

    /// <summary>
    /// JSON Pointer (RFC 6901) path targeting the property or element to operate on.
    /// Examples: <c>/name</c>, <c>/address/city</c>, <c>/items/0</c>.
    /// The convenience prefix <c>$.</c> (e.g. <c>$.name</c>) is also accepted and
    /// normalized to JSON Pointer internally.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The JSON value to set or merge. Required for <see cref="JsonTransformType.Set"/>
    /// and <see cref="JsonTransformType.Merge"/> operations; ignored for
    /// <see cref="JsonTransformType.Extract"/> and <see cref="JsonTransformType.Delete"/>.
    /// </summary>
    public string? Value { get; init; }
}
