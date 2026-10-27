using FluentAssertions;
using Shop.Application.Catalog;
using Shop.Application.Catalog.Queries;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Tests.Catalog;

public class ProductQueryTests
{
    private readonly Harness _h = new();

    private static ProductDto Dto(string name, decimal price = 10m, int n = 0) => new(
        Guid.NewGuid(), $"SKU-{name}", name, null, new MoneyDto(price, "USD"), "Cat", 1, true,
        new DateTimeOffset(2026, 1, 1, 0, 0, n, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 0, 0, n, TimeSpan.Zero));

    [Fact]
    public async Task Get_by_id_returns_null_success_when_missing()
    {
        var result = await _h.Dispatcher.Send(new GetProductByIdQuery(Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Get_by_sku_returns_dto()
    {
        var dto = Dto("A");
        _h.Reads.Items.Add(dto);

        var result = await _h.Dispatcher.Send(new GetProductBySkuQuery("SKU-A"));

        result.Value.Should().Be(dto);
    }

    [Theory]
    [InlineData(500, 100)]
    [InlineData(100, 100)]
    [InlineData(7, 7)]
    public async Task List_caps_first_at_100_and_asks_for_one_extra_row(int first, int expectedFirst)
    {
        await _h.Dispatcher.Send(new ListProductsQuery(first));

        _h.Reads.LastRequest!.Take.Should().Be(expectedFirst + 1);
    }

    [Fact]
    public async Task List_negative_first_fails_validation()
    {
        var result = await _h.Dispatcher.Send(new ListProductsQuery(-1));

        result.Errors.Should().ContainSingle().Which.Field.Should().Be("first");
    }

    [Fact]
    public async Task List_builds_edges_page_info_and_total()
    {
        _h.Reads.Items.AddRange([Dto("A"), Dto("B"), Dto("C")]);

        var page = (await _h.Dispatcher.Send(new ListProductsQuery(2))).Value!;

        page.Edges.Should().HaveCount(2);
        page.HasNextPage.Should().BeTrue();
        page.TotalCount.Should().Be(3);
        page.StartCursor.Should().Be(page.Edges[0].Cursor);
        page.EndCursor.Should().Be(page.Edges[1].Cursor);
    }

    [Fact]
    public async Task List_last_page_has_no_next_page_and_empty_result_has_null_cursors()
    {
        _h.Reads.Items.Add(Dto("A"));
        var one = (await _h.Dispatcher.Send(new ListProductsQuery(5))).Value!;
        one.HasNextPage.Should().BeFalse();

        _h.Reads.Items.Clear();
        var none = (await _h.Dispatcher.Send(new ListProductsQuery(5))).Value!;
        none.Edges.Should().BeEmpty();
        none.StartCursor.Should().BeNull();
        none.EndCursor.Should().BeNull();
    }

    [Fact]
    public async Task List_passes_decoded_cursor_and_filter_to_read_service()
    {
        _h.Reads.Items.AddRange([Dto("A"), Dto("B")]);
        var sort = new ProductSort(ProductSortField.Name, SortDirection.Asc);
        var first = (await _h.Dispatcher.Send(new ListProductsQuery(1, null, null, sort))).Value!;

        var filter = new ProductFilter(Category: "Cat");
        await _h.Dispatcher.Send(new ListProductsQuery(1, first.EndCursor, filter, sort));

        _h.Reads.LastRequest!.After!.Id.Should().Be(first.Edges[0].Node.Id);
        _h.Reads.LastRequest.Filter.Should().Be(filter);
    }

    [Fact]
    public async Task List_rejects_malformed_cursor()
    {
        var result = await _h.Dispatcher.Send(new ListProductsQuery(5, "not-a-cursor"));

        result.Errors.Should().ContainSingle()
            .Which.Should().Match<Abstractions.AppError>(e => e.Code == DomainErrorCodes.ValidationFailed && e.Field == "after");
    }

    [Fact]
    public async Task List_rejects_cursor_issued_for_a_different_sort()
    {
        _h.Reads.Items.AddRange([Dto("A"), Dto("B")]);
        var byName = (await _h.Dispatcher.Send(new ListProductsQuery(1, null, null, new ProductSort(ProductSortField.Name)))).Value!;

        var result = await _h.Dispatcher.Send(
            new ListProductsQuery(1, byName.EndCursor, null, new ProductSort(ProductSortField.Price)));

        result.Errors.Should().ContainSingle().Which.Field.Should().Be("after");
    }
}
