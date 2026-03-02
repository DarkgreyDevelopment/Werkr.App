namespace Werkr.Data.Entities.Interfaces;

/// <summary>
/// Generic interface for entities with a typed primary key.
/// </summary>
/// <typeparam name="T">The type of the primary key.</typeparam>
public interface IKey<T> {
    /// <summary>Primary key.</summary>
    T Id { get; set; }
}
