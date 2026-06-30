namespace InvoiceManager.Core;

/// <summary>
/// The absence of a value. Used as a case of <see cref="Option{T}"/> so that
/// missing values are represented explicitly rather than with <c>null</c>.
/// Prefer the shared <see cref="Option.None"/> value over constructing instances.
/// </summary>
public readonly record struct None;

/// <summary>
/// Holds either a value of type <typeparamref name="T"/> or <see cref="None"/>.
/// A value converts to an <see cref="Option{T}"/> implicitly, so callers can
/// pass a value or <see cref="Option.None"/> directly and match on the result.
/// </summary>
public union Option<T>(T, None);

/// <summary>Non-generic helpers for <see cref="Option{T}"/>.</summary>
public static class Option
{
    /// <summary>
    /// The shared <see cref="Core.None"/> value. It converts implicitly to any
    /// <see cref="Option{T}"/>, so a single instance serves every option type.
    /// </summary>
    public static None None { get; } = default;
}
