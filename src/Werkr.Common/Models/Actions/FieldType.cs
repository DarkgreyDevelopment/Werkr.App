namespace Werkr.Common.Models.Actions;

/// <summary>Describes the field type rendered in the parameter editor.</summary>
public enum FieldType {
    /// <summary>Single-line text input.</summary>
    Text,
    /// <summary>Multi-line text area.</summary>
    TextArea,
    /// <summary>Numeric input (integer or decimal).</summary>
    Number,
    /// <summary>Boolean toggle / checkbox.</summary>
    Bool,
    /// <summary>Dropdown select from a fixed set of options.</summary>
    Select,
    /// <summary>Dynamic list of string values (add/remove rows).</summary>
    StringArray,
    /// <summary>Dynamic list of integer values (add/remove rows).</summary>
    IntArray,
    /// <summary>Dynamic list of key-value string pairs (add/remove rows).</summary>
    KeyValueMap,
    /// <summary>Dynamic list of structured objects, each defined by <see cref="FieldDescriptor.SubFields"/>.</summary>
    ObjectArray,
}
