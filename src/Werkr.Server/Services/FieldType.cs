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
    Select,
    /// <summary>
    /// Raw JSON value (object or array) entered in a multi-line text area.
    /// The editor embeds the parsed JSON inline rather than emitting it as a string,
    /// making it suitable for array or nested-object parameters.
    /// </summary>
    Json
}
