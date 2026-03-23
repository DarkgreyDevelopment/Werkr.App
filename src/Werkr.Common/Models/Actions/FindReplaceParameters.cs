namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the FindReplace action.</summary>
public sealed record FindReplaceParameters {
    /// <summary>Full path of the file to perform find-and-replace on.</summary>
    public required string Path { get; init; }

    /// <summary>The string or regex pattern to find.</summary>
    public required string Find { get; init; }

    /// <summary>The replacement string.</summary>
    public required string Replace { get; init; }

    /// <summary>Whether <see cref="Find"/> is a regular expression.</summary>
    public bool IsRegex { get; init; }

    /// <summary>Whether the find operation is case-sensitive. Default: true.</summary>
    public bool CaseSensitive { get; init; } = true;

    /// <summary>Text encoding (e.g. "utf-8", "ascii"). Default: "utf-8".</summary>
    public string Encoding { get; init; } = "utf-8";
}
