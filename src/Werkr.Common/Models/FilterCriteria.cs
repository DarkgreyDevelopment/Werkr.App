namespace Werkr.Common.Models;

/// <summary>Holds the current filter values keyed by field name.</summary>
public sealed class FilterCriteria {

    /// <summary>Filter values keyed by <see cref="FilterField.Key"/>.</summary>
    public Dictionary<string, string?> Values { get; set; } = [];

    /// <summary>Gets the value for the given <paramref name="key"/>, or <see langword="null"/> if absent.</summary>
    public string? Get( string key )
        => Values.TryGetValue( key, out string? v ) ? v : null;
}
