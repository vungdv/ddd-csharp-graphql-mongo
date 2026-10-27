using Shop.Domain.SharedKernel;

namespace Shop.Domain.Ordering;

/// <summary>
/// An order with the same id or order number already exists. Carries CONCURRENCY_CONFLICT (no dedicated spec code) and is
/// distinguishable so callers can retry with a freshly generated number.
/// </summary>
public sealed class DuplicateOrderException(string message) : DomainException(DomainErrorCodes.ConcurrencyConflict, message);
