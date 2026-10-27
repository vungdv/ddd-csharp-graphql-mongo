using System.Text.RegularExpressions;
using FluentAssertions;
using Shop.Domain.Catalog;
using Shop.Domain.Ordering;
using Shop.Domain.SharedKernel;

namespace Shop.Domain.Tests.Ordering;

public class OrderTests
{
    private sealed class FixedNumbers(string suffix = "ABC123") : IOrderNumberGenerator
    {
        public string Next(DateTimeOffset now) => $"ORD-{now.UtcDateTime:yyyyMMdd}-{suffix}";
    }

    private readonly TestClock _clock = new();

    private static CustomerInfo Customer() => CustomerInfo.Create("Ada Lovelace", "ada@example.com");

    private static Address Addr() => Address.Create("1 Main St", null, "London", null, "N1 1AA", "GB");

    private static OrderLine Line(int qty = 1, decimal price = 10m, string currency = "USD", ProductId? id = null, string sku = "SKU-1") =>
        OrderLine.Create(id ?? ProductId.New(), Sku.Create(sku), "Thing", Money.Of(price, currency), qty);

    private Order Place(params OrderLine[] lines) => Order.Place(Customer(), Addr(), lines, _clock, new FixedNumbers());

    private static string CodeOf(Action act) => act.Should().Throw<DomainException>().Which.Code;

    [Fact]
    public void Place_sets_state_and_computes_total()
    {
        var a = Line(2, 10.50m, sku: "A-1");
        var b = Line(3, 1.25m, sku: "B-1");

        var order = Place(a, b);

        order.Id.Value.Should().NotBe(Guid.Empty);
        order.Status.Should().Be(OrderStatus.Placed);
        order.Total.Should().Be(Money.Of(24.75m));
        order.Lines.Should().HaveCount(2);
        order.Lines.Sum(l => l.LineTotal.Amount).Should().Be(order.Total.Amount);
        order.PlacedAt.Should().Be(_clock.UtcNow);
        order.CancelledAt.Should().BeNull();
        order.CancellationReason.Should().BeNull();
        order.Version.Should().Be(0);
        order.OrderNumber.Should().Be("ORD-20260101-ABC123");
    }

    [Fact]
    public void Place_raises_OrderPlaced_with_items()
    {
        var a = Line(2);
        var order = Place(a);

        var evt = order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrderPlaced>().Subject;
        evt.OrderId.Should().Be(order.Id);
        evt.OrderNumber.Should().Be(order.OrderNumber);
        evt.Total.Should().Be(order.Total);
        evt.Items.Should().Equal(new OrderedItem(a.ProductId, 2));
        evt.OccurredAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public void Default_order_number_matches_format_and_uses_clock_date()
    {
        _clock.UtcNow = new DateTimeOffset(2026, 3, 9, 23, 59, 0, TimeSpan.FromHours(-5)); // 2026-03-10 UTC
        var order = Order.Place(Customer(), Addr(), [Line()], _clock);

        order.OrderNumber.Should().MatchRegex(@"^ORD-\d{8}-[A-Z0-9]{6}$");
        order.OrderNumber.Should().StartWith("ORD-20260310-");
    }

    [Fact]
    public void Random_generator_produces_varied_valid_numbers()
    {
        var gen = new RandomOrderNumberGenerator();
        var numbers = Enumerable.Range(0, 50).Select(_ => gen.Next(_clock.UtcNow)).ToList();

        numbers.Should().OnlyContain(n => Regex.IsMatch(n, @"^ORD-20260101-[A-Z0-9]{6}$"));
        numbers.Distinct().Count().Should().BeGreaterThan(1);
    }

    [Fact]
    public void Place_rejects_empty_lines() =>
        CodeOf(() => Place()).Should().Be(DomainErrorCodes.OrderEmpty);

    [Fact]
    public void Place_rejects_null_line_element() =>
        CodeOf(() => Place(Line(), null!)).Should().Be(DomainErrorCodes.ValidationFailed);

    [Fact]
    public void Place_rejects_duplicate_product()
    {
        var id = ProductId.New();
        CodeOf(() => Place(Line(id: id), Line(id: id))).Should().Be(DomainErrorCodes.OrderDuplicateProduct);
    }

    [Fact]
    public void Place_accepts_50_lines_and_rejects_51()
    {
        var ok = Enumerable.Range(0, 50).Select(_ => Line()).ToArray();
        Place(ok).Lines.Should().HaveCount(50);

        var tooMany = Enumerable.Range(0, 51).Select(_ => Line()).ToArray();
        CodeOf(() => Place(tooMany)).Should().Be(DomainErrorCodes.ValidationFailed);
    }

    [Fact]
    public void Place_rejects_mixed_currencies() =>
        CodeOf(() => Place(Line(currency: "USD"), Line(currency: "EUR"))).Should().Be(DomainErrorCodes.CurrencyMismatch);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Line_rejects_quantity_out_of_range(int qty) =>
        CodeOf(() => Line(qty)).Should().Be(DomainErrorCodes.ValidationFailed);

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public void Line_accepts_quantity_bounds(int qty) => Line(qty).Quantity.Should().Be(qty);

    [Fact]
    public void Line_total_is_unit_price_times_quantity() =>
        Line(3, 2.50m).LineTotal.Should().Be(Money.Of(7.50m));

    [Fact]
    public void Line_requires_product_name()
    {
        var act = () => OrderLine.Create(ProductId.New(), Sku.Create("SKU-1"), " ", Money.Of(1m), 1);
        CodeOf(() => act()).Should().Be(DomainErrorCodes.ValidationFailed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Customer_requires_name(string? name) =>
        CodeOf(() => CustomerInfo.Create(name, "a@b.com")).Should().Be(DomainErrorCodes.ValidationFailed);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("a@b")]
    [InlineData("@b.com")]
    [InlineData("a b@c.com")]
    [InlineData("a@@b.com")]
    public void Customer_rejects_invalid_email(string? email)
    {
        var act = () => CustomerInfo.Create("Ada", email);
        act.Should().Throw<DomainException>().Which.Field.Should().Be("customer.email");
    }

    [Fact]
    public void Customer_normalizes_email_and_trims_name()
    {
        var c = CustomerInfo.Create("  Ada ", " Ada@Example.COM ");
        c.Name.Should().Be("Ada");
        c.Email.Should().Be("ada@example.com");
    }

    [Theory]
    [InlineData("line1")]
    [InlineData("city")]
    [InlineData("postalCode")]
    [InlineData("country")]
    public void Address_requires_mandatory_fields(string missing)
    {
        string? Pick(string name) => name == missing ? " " : "x";
        var act = () => Address.Create(Pick("line1"), null, Pick("city"), null, Pick("postalCode"), Pick("country"));
        act.Should().Throw<DomainException>().Which.Field.Should().Be($"shippingAddress.{missing}");
    }

    [Fact]
    public void Address_optional_fields_may_be_missing_or_set()
    {
        var a = Address.Create("1 Main", "", "London", null, "N1", "GB");
        a.Line2.Should().BeNull();
        a.State.Should().BeNull();
        Address.Create("1 Main", "Flat 2", "NYC", "NY", "10001", "US").State.Should().Be("NY");
    }

    [Fact]
    public void Cancel_transitions_and_raises_event()
    {
        var a = Line(2);
        var order = Place(a);
        order.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromHours(1));

        order.Cancel("  changed my mind ", _clock);

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancelledAt.Should().Be(_clock.UtcNow);
        order.CancellationReason.Should().Be("changed my mind");
        var evt = order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrderCancelled>().Subject;
        evt.Reason.Should().Be("changed my mind");
        evt.Items.Should().Equal(new OrderedItem(a.ProductId, 2));
        evt.OrderNumber.Should().Be(order.OrderNumber);
    }

    [Fact]
    public void Cancel_without_reason_stores_null()
    {
        var order = Place(Line());
        order.Cancel(null, _clock);
        order.CancellationReason.Should().BeNull();
    }

    [Fact]
    public void Cancel_twice_fails_without_changing_state()
    {
        var order = Place(Line());
        order.Cancel("first", _clock);
        var cancelledAt = order.CancelledAt;
        _clock.Advance(TimeSpan.FromMinutes(5));
        var eventCount = order.DomainEvents.Count;

        CodeOf(() => order.Cancel("second", _clock)).Should().Be(DomainErrorCodes.OrderAlreadyCancelled);

        order.CancelledAt.Should().Be(cancelledAt);
        order.CancellationReason.Should().Be("first");
        order.DomainEvents.Should().HaveCount(eventCount);
    }

    [Fact]
    public void Cancel_rejects_overlong_reason()
    {
        var order = Place(Line());
        CodeOf(() => order.Cancel(new string('x', Order.MaxReasonLength + 1), _clock)).Should().Be(DomainErrorCodes.ValidationFailed);
        order.Status.Should().Be(OrderStatus.Placed);
    }

    [Fact]
    public void Lines_cannot_be_mutated_through_caller_list()
    {
        var source = new List<OrderLine> { Line() };
        var order = Order.Place(Customer(), Addr(), source, _clock);
        source.Add(Line());
        order.Lines.Should().HaveCount(1);
    }
}
