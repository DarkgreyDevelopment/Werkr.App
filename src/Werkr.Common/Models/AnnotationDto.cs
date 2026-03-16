using System.ComponentModel.DataAnnotations;

namespace Werkr.Common.Models;

/// <summary>DTO for a DAG canvas sticky-note annotation.</summary>
public sealed record AnnotationDto {

    /// <summary>Unique identifier for the annotation.</summary>
    [Required]
    public Guid Id { get; init; }

    /// <summary>Annotation text content (max 500 chars).</summary>
    [Required, MaxLength( 500 )]
    public string Text { get; init; } = "";

    /// <summary>X position on the canvas.</summary>
    [Range( -10000, 50000 )]
    public double X { get; init; }

    /// <summary>Y position on the canvas.</summary>
    [Range( -10000, 50000 )]
    public double Y { get; init; }

    /// <summary>Width of the annotation card.</summary>
    [Range( 80, 800 )]
    public double Width { get; init; }

    /// <summary>Height of the annotation card.</summary>
    [Range( 40, 600 )]
    public double Height { get; init; }

    /// <summary>Background color as a hex string (e.g., #fef3cd).</summary>
    [RegularExpression( @"^#[0-9a-fA-F]{6}$" )]
    public string Color { get; init; } = "#fef3cd";
}
