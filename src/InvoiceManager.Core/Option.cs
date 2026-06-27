namespace InvoiceManager.Core;

/// <summary>
/// The absence of a value. Used as a case of <see cref="Option{T}"/> so that
/// missing values are represented explicitly rather than with <c>null</c>.
/// </summary>
public readonly record struct None;

/// <summary>
/// Holds either a value of type <typeparamref name="T"/> or <see cref="None"/>.
/// A value converts to an <see cref="Option{T}"/> implicitly, so callers can
/// pass a value or <c>new None()</c> directly and match on the result.
/// </summary>
public union Option<T>(T, None);
