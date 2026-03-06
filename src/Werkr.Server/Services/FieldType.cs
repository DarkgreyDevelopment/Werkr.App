namespace Werkr.Server.Services;

/// <summary>Describes the field type rendered in the parameter editor.</summary>
public enum FieldType {
    /// <summary>Single-line text input.</summary>
    Text,
    /// <summary>Multi-line text area.</summary>
    TextArea,
    /// <summary>Numeric input.</summary>
    Number,
    /// <summary>Boolean toggle / checkbox.</summary>
    Bool,
    /// <summary>Dropdown select from a fixed set of options.</summary>
    Select
}
