using NodaMoney;

namespace InvoiceManager.Core;

/// <summary>Optional criteria used when a source or OneDrive candidate has a predictable amount.</summary>
public sealed record AmountMatchingCriteria(Money Amount, decimal AmountTolerance);
