using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions;
using Shop.Application.Ordering;
using Shop.Application.Ordering.Queries;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Tests.Ordering;

public class OrderQueryTests
{
    private sealed class FakeOrderReads : IOrderReadService
    {
        public List<OrderDto> Items { get; } = [];

        public OrderPageRequest? Last { get; private set; }

        public Task<OrderDto?> GetById(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(o => o.Id == id));

        public Task<OrderDto?> GetByOrderNumber(string n, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(o => o.OrderNumber == n));

        public Task<OrderPageSlice> List(OrderPageRequest request, CancellationToken ct = default)
        {
            Last = request;
            return Task.FromResult(new OrderPageSlice(Items.Take(request.Take).ToList(), Items.Count));
        }
    }

    private readonly FakeOrderReads _reads = new();
    private readonly IDispatcher _dispatcher;

    public OrderQueryTests()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IOrderReadService>(_reads);
        _dispatcher = services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();
    }

    private static OrderDto Dto(int i) => new(
        Guid.NewGuid(),
        $"ORD-20260101-{i:D6}",
        new CustomerDto("n", "e@x.com"),
        new AddressDto("l", null, "c", null, "p", "GB"),
        [],
        Shop.Domain.Ordering.OrderStatus.Placed,
        new Shop.Application.Catalog.MoneyDto(1m, "USD"),
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(-i),
        null,
        null);

    [Fact]
    public async Task List_caps_first_at_100_and_fetches_one_extra_to_detect_next_page()
    {
        _reads.Items.AddRange(Enumerable.Range(0, 150).Select(Dto));

        var result = await _dispatcher.Send(new ListOrdersQuery(First: 1000));

        _reads.Last!.Take.Should().Be(101);
        result.Value!.Edges.Should().HaveCount(100);
        result.Value.HasNextPage.Should().BeTrue();
        result.Value.TotalCount.Should().Be(150);
    }

    [Fact]
    public async Task List_normalises_customer_email_and_rejects_bad_cursors_and_inverted_ranges()
    {
        await _dispatcher.Send(new ListOrdersQuery(Filter: new OrderFilter(CustomerEmail: "  Ada@Example.COM ")));
        _reads.Last!.Filter.CustomerEmail.Should().Be("ada@example.com");

        var badCursor = await _dispatcher.Send(new ListOrdersQuery(After: "garbage"));
        var inverted = await _dispatcher.Send(new ListOrdersQuery(
            Filter: new OrderFilter(PlacedFrom: DateTimeOffset.UtcNow, PlacedTo: DateTimeOffset.UtcNow.AddDays(-1))));

        badCursor.Errors.Single().Should().Be(new AppError(DomainErrorCodes.ValidationFailed, "The 'after' cursor is invalid.", "after"));
        inverted.Errors.Single().Code.Should().Be(DomainErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Cursor_round_trips_and_resumes_after_the_last_edge()
    {
        _reads.Items.AddRange(Enumerable.Range(0, 3).Select(Dto));
        var first = (await _dispatcher.Send(new ListOrdersQuery(First: 2))).Value!;

        await _dispatcher.Send(new ListOrdersQuery(First: 2, After: first.EndCursor));

        _reads.Last!.After.Should().Be(OrderCursor.For(_reads.Items[1]));
    }
}
