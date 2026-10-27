using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Ordering.Commands;
using Shop.Domain.Catalog;
using Shop.Domain.Ordering;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Tests.Ordering;

public class OrderCommandTests
{
    private readonly OrderHarness _h = new();

    private static PlaceOrderCommand Place(params (Product Product, int Qty)[] lines) =>
        PlaceRaw(lines.Select(l => new OrderLineInput(l.Product.Id.Value, l.Qty)).ToArray());

    private static PlaceOrderCommand PlaceRaw(params OrderLineInput[] lines) =>
        new(
            new CustomerInput("Ada Lovelace", "Ada@Example.com"),
            new AddressInput("1 Main St", null, "London", null, "N1 1AA", "GB"),
            lines);

    // ---- PlaceOrder ----

    [Fact]
    public async Task Place_order_snapshots_lines_totals_decreases_stock_and_persists()
    {
        var shirt = _h.AddProduct("SHIRT-1", 19.99m, stock: 10);
        var mug = _h.AddProduct("MUG-1", 5.5m, stock: 4);

        var result = await _h.Dispatcher.Send(Place((shirt, 2), (mug, 3)));

        result.IsSuccess.Should().BeTrue();
        var order = result.Value!;
        order.Status.Should().Be(OrderStatus.Placed);
        order.OrderNumber.Should().MatchRegex("^ORD-20260101-[A-Z0-9]{6}$");
        order.Customer.Email.Should().Be("ada@example.com");
        order.Lines.Select(l => (l.Sku, l.ProductName, l.UnitPrice.Amount, l.Quantity, l.LineTotal.Amount))
            .Should().Equal(("SHIRT-1", "Name SHIRT-1", 19.99m, 2, 39.98m), ("MUG-1", "Name MUG-1", 5.5m, 3, 16.5m));
        order.Total.Amount.Should().Be(56.48m);
        _h.Stock(shirt).Should().Be(8);
        _h.Stock(mug).Should().Be(1);
        _h.Store.Orders.Should().ContainKey(new OrderId(order.Id));
        _h.Store.Orders[new OrderId(order.Id)].DomainEvents.Should().BeEmpty("events are cleared after dispatch");
    }

    [Fact]
    public async Task Place_order_with_inactive_product_fails_with_PRODUCT_INACTIVE_and_changes_nothing()
    {
        var ok = _h.AddProduct("OK-1", stock: 5);
        var off = _h.AddProduct("OFF-1", stock: 5, active: false);

        var result = await _h.Dispatcher.Send(Place((ok, 1), (off, 1)));

        result.Errors.Should().ContainSingle().Which.Should().Match<Shop.Application.Abstractions.AppError>(
            e => e.Code == DomainErrorCodes.ProductInactive && e.Field == "lines[1].productId");
        _h.Store.Orders.Should().BeEmpty();
        _h.Stock(ok).Should().Be(5);
        _h.Stock(off).Should().Be(5);
    }

    [Fact]
    public async Task Place_order_with_missing_product_fails_with_PRODUCT_NOT_FOUND()
    {
        var ok = _h.AddProduct("OK-1", stock: 5);

        var result = await _h.Dispatcher.Send(PlaceRaw(new OrderLineInput(ok.Id.Value, 1), new OrderLineInput(Guid.NewGuid(), 1)));

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(DomainErrorCodes.ProductNotFound);
        _h.Store.Orders.Should().BeEmpty();
        _h.Stock(ok).Should().Be(5);
    }

    [Fact]
    public async Task Insufficient_stock_on_a_later_line_leaves_all_stock_unchanged_and_creates_no_order()
    {
        var first = _h.AddProduct("A-1", stock: 5);
        var second = _h.AddProduct("B-1", stock: 1);

        var result = await _h.Dispatcher.Send(Place((first, 3), (second, 2)));

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(DomainErrorCodes.InsufficientStock);
        _h.Stock(first).Should().Be(5, "the transaction rolled back the first line's decrease");
        _h.Stock(second).Should().Be(1);
        _h.Store.Orders.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_input_reports_nested_field_names_matching_the_input_shape()
    {
        var p = _h.AddProduct("A-1");
        var command = new PlaceOrderCommand(
            new CustomerInput("", "not-an-email"),
            new AddressInput("1 Main St", null, "", null, "N1", "GB"),
            [new OrderLineInput(p.Id.Value, 0)]);

        var result = await _h.Dispatcher.Send(command);

        result.Errors.Should().OnlyContain(e => e.Code == DomainErrorCodes.ValidationFailed);
        result.Errors.Select(e => e.Field).Should()
            .BeEquivalentTo("customer.name", "customer.email", "shippingAddress.city", "lines[0].quantity");
        _h.Store.Transactions.Should().Be(0, "validation runs before any transaction");
    }

    [Fact]
    public async Task Empty_lines_and_duplicate_products_map_to_domain_codes()
    {
        var p = _h.AddProduct("A-1", stock: 9);

        var empty = await _h.Dispatcher.Send(PlaceRaw());
        var dup = await _h.Dispatcher.Send(Place((p, 1), (p, 2)));

        empty.Errors.Single().Code.Should().Be(DomainErrorCodes.OrderEmpty);
        dup.Errors.Single().Code.Should().Be(DomainErrorCodes.OrderDuplicateProduct);
        _h.Stock(p).Should().Be(9);
    }

    [Fact]
    public async Task Duplicate_order_number_restarts_the_whole_transaction_with_a_new_number()
    {
        var p = _h.AddProduct("A-1", stock: 10);
        _h.Numbers.Queued.Enqueue("ORD-20260101-AAAAAA");
        (await _h.Dispatcher.Send(Place((p, 1)))).IsSuccess.Should().BeTrue();
        _h.Numbers.Queued.Enqueue("ORD-20260101-AAAAAA"); // collides
        _h.Numbers.Queued.Enqueue("ORD-20260101-BBBBBB");

        var result = await _h.Dispatcher.Send(Place((p, 2)));

        result.IsSuccess.Should().BeTrue();
        result.Value!.OrderNumber.Should().Be("ORD-20260101-BBBBBB");
        _h.Stock(p).Should().Be(7, "the aborted attempt's stock decrease was rolled back; only 1 + 2 were taken");
        _h.Store.Orders.Should().HaveCount(2);
    }

    [Fact]
    public async Task Persistent_duplicate_order_number_gives_up_after_bounded_retries_with_CONCURRENCY_CONFLICT()
    {
        var p = _h.AddProduct("A-1", stock: 10);
        _h.Numbers.Always = "ORD-20260101-AAAAAA";
        (await _h.Dispatcher.Send(Place((p, 1)))).IsSuccess.Should().BeTrue();
        var callsBefore = _h.Numbers.Calls;

        var result = await _h.Dispatcher.Send(Place((p, 2)));

        result.Errors.Single().Code.Should().Be(DomainErrorCodes.ConcurrencyConflict);
        (_h.Numbers.Calls - callsBefore).Should().Be(4, "one attempt plus three retries");
        _h.Stock(p).Should().Be(9);
        _h.Store.Orders.Should().HaveCount(1);
    }

    // ---- CancelOrder ----

    [Fact]
    public async Task Cancel_order_marks_cancelled_stores_reason_and_restores_stock()
    {
        var shirt = _h.AddProduct("SHIRT-1", stock: 10);
        var mug = _h.AddProduct("MUG-1", stock: 4);
        var placed = (await _h.Dispatcher.Send(Place((shirt, 2), (mug, 3)))).Value!;
        _h.Clock.UtcNow = _h.Clock.UtcNow.AddHours(1);

        var result = await _h.Dispatcher.Send(new CancelOrderCommand(placed.Id, "changed my mind"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(OrderStatus.Cancelled);
        result.Value.CancellationReason.Should().Be("changed my mind");
        result.Value.CancelledAt.Should().Be(_h.Clock.UtcNow);
        _h.Stock(shirt).Should().Be(10);
        _h.Stock(mug).Should().Be(4);
    }

    [Fact]
    public async Task Cancelling_twice_returns_ORDER_ALREADY_CANCELLED_and_restores_stock_only_once()
    {
        var p = _h.AddProduct("A-1", stock: 10);
        var placed = (await _h.Dispatcher.Send(Place((p, 4)))).Value!;
        (await _h.Dispatcher.Send(new CancelOrderCommand(placed.Id, null))).IsSuccess.Should().BeTrue();

        var again = await _h.Dispatcher.Send(new CancelOrderCommand(placed.Id, null));

        again.Errors.Single().Code.Should().Be(DomainErrorCodes.OrderAlreadyCancelled);
        _h.Stock(p).Should().Be(10);
    }

    private sealed class FailingRelease : Shop.Application.Ordering.IStockReservation
    {
        public int Calls { get; private set; }

        public Task Reserve(IReadOnlyCollection<Shop.Application.Ordering.StockLine> lines, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task Release(IReadOnlyCollection<Shop.Application.Ordering.StockLine> lines, CancellationToken ct = default)
        {
            Calls++;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class RecordingHandler : Shop.Application.Events.IDomainEventHandler<OrderCancelled>
    {
        public int Calls { get; private set; }

        public Task Handle(OrderCancelled domainEvent, CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<Shop.Application.Events.DomainEventDispatcher>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, Exception? Ex)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception));
    }

    [Fact]
    public async Task Failing_stock_release_does_not_fail_the_cancel_is_logged_and_other_handlers_still_run()
    {
        var release = new FailingRelease();
        var other = new RecordingHandler();
        var logger = new CapturingLogger();
        var h = new OrderHarness(s =>
        {
            s.AddSingleton<Shop.Application.Ordering.IStockReservation>(release);
            s.AddSingleton<Shop.Application.Events.IDomainEventHandler<OrderCancelled>>(other);
            s.AddSingleton<Microsoft.Extensions.Logging.ILogger<Shop.Application.Events.DomainEventDispatcher>>(logger);
        });
        var p = h.AddProduct("A-1", stock: 10);
        var order = Order.Place(
            CustomerInfo.Create("Ada", "ada@example.com"),
            Address.Create("1 Main St", null, "London", null, "N1", "GB"),
            [OrderLine.Create(p.Id, p.Sku, "n", Money.Of(1m), 2)],
            h.Clock);
        h.Store.Orders[order.Id] = order;
        order.ClearDomainEvents();

        var result = await h.Dispatcher.Send(new CancelOrderCommand(order.Id.Value, "x"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(OrderStatus.Cancelled);
        release.Calls.Should().Be(1);
        other.Calls.Should().Be(1);
        logger.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error && e.Ex is InvalidOperationException);
    }

    [Fact]
    public async Task Cancelling_unknown_order_returns_ORDER_NOT_FOUND()
    {
        var result = await _h.Dispatcher.Send(new CancelOrderCommand(Guid.NewGuid(), null));

        result.Errors.Single().Should().Match<Shop.Application.Abstractions.AppError>(
            e => e.Code == DomainErrorCodes.OrderNotFound && e.Field == "id");
    }
}
