using Shop.Domain.Ordering;

namespace Shop.Application.Ordering;

/// <summary>Read-side port (CQRS) for orders: projects straight from storage into DTOs.</summary>
public interface IOrderReadService
{
    Task<OrderDto?> GetById(Guid id, CancellationToken ct = default);

    Task<OrderDto?> GetByOrderNumber(string orderNumber, CancellationToken ct = default);

    /// <summary>Returns up to <see cref="OrderPageRequest.Take"/> items after the cursor plus the filtered total.</summary>
    Task<OrderPageSlice> List(OrderPageRequest request, CancellationToken ct = default);
}

/// <summary>
/// <see cref="CustomerEmail"/> is expected lower-cased (emails are stored lower-cased). <see cref="PlacedFrom"/> and
/// <see cref="PlacedTo"/> are both inclusive. Sort is fixed: placedAt desc, id desc.
/// </summary>
public sealed record OrderFilter(
    OrderStatus? Status = null,
    string? CustomerEmail = null,
    DateTimeOffset? PlacedFrom = null,
    DateTimeOffset? PlacedTo = null);

public sealed record OrderPageRequest(int Take, OrderFilter Filter, OrderCursor? After);

public sealed record OrderPageSlice(IReadOnlyList<OrderDto> Items, int TotalCount);
