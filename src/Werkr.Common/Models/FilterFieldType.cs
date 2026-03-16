namespace Werkr.Common.Models;

/// <summary>Supported control types for filter fields.</summary>
public enum FilterFieldType {

    /// <summary>Drop-down select with predefined options.</summary>
    Dropdown,

    /// <summary>Date picker.</summary>
    Date,

    /// <summary>Free-form text input.</summary>
    Text,

    /// <summary>Numeric input.</summary>
    Number,
}
