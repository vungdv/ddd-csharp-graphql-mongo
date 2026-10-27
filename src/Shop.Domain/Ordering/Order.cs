using Shop.Domain.SharedKernel;

namespace Shop.Domain.Ordering;

public sealed class Order : AggregateRoot<OrderId>
{
    public const int MaxLines = 50;
    public const int MaxReasonLength = 500;

    // Not readonly: persistence rehydration assigns the field (mapped as "lines").
    private List<OrderLine> _lines = [];

    // Used by persistence rehydration only.
    private Order()
    {
        OrderNumber = null!;
        Customer = null!;
        ShippingAddress = null!;
        Total = null!;
    }

    public string OrderNumber { get; private set; }

    public CustomerInfo Customer { get; private set; }

    public Address ShippingAddress { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public OrderStatus Status { get; private set; }

    /// <summary>Sum of line totals, computed at placement.</summary>
    public Money Total { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public static Order Place(CustomerInfo customer, Address shippingAddress, IEnumerable<OrderLine> lines, IClock clock) =>
        Place(customer, shippingAddress, lines, clock, RandomOrderNumberGenerator.Instance);

    public static Order Place(
        CustomerInfo customer,
        Address shippingAddress,
        IEnumerable<OrderLine> lines,
        IClock clock,
        IOrderNumberGenerator numberGenerator)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(shippingAddress);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(numberGenerator);

        var list = lines.ToList();
        if (list.Count == 0)
        {
            throw new DomainException(DomainErrorCodes.OrderEmpty, "An order must contain at least one line.", "lines");
        }

        if (list.Any(l => l is null))
        {
            throw DomainException.Validation("lines", "Order lines must not contain null entries.");
        }

        if (list.Count > MaxLines)
        {
            throw DomainException.Validation("lines", $"An order can contain at most {MaxLines} lines.");
        }

        var duplicate = list.GroupBy(l => l.ProductId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new DomainException(
                DomainErrorCodes.OrderDuplicateProduct,
                $"Product {duplicate.Key} appears on more than one line.",
                "lines");
        }

        var currency = list[0].UnitPrice.Currency;
        if (list.Any(l => l.UnitPrice.Currency != currency))
        {
            throw new DomainException(
                DomainErrorCodes.CurrencyMismatch,
                "All order lines must use the same currency.",
                "lines");
        }

        var now = clock.UtcNow;
        var total = list.Aggregate(Money.Of(0m, currency), (sum, l) => sum.Add(l.LineTotal));
        var order = new Order
        {
            Id = OrderId.New(),
            OrderNumber = numberGenerator.Next(now),
            Customer = customer,
            ShippingAddress = shippingAddress,
            Status = OrderStatus.Placed,
            Total = total,
            PlacedAt = now,
        };
        order._lines.AddRange(list);
        order.Raise(new OrderPlaced(order.Id, order.OrderNumber, total, order.Items(), now));
        return order;
    }

    public void Cancel(string? reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Status != OrderStatus.Placed)
        {
            throw new DomainException(
                DomainErrorCodes.OrderAlreadyCancelled,
                $"Order {OrderNumber} is already cancelled.");
        }

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed is { Length: > MaxReasonLength })
        {
            throw DomainException.Validation("reason", $"Reason must be at most {MaxReasonLength} characters.");
        }

        var now = clock.UtcNow;
        Status = OrderStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = trimmed;
        Raise(new OrderCancelled(Id, OrderNumber, trimmed, Items(), now));
    }

    private List<OrderedItem> Items() => _lines.Select(l => new OrderedItem(l.ProductId, l.Quantity)).ToList();
}
