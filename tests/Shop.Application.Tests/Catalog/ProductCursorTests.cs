using System.Text;
using FluentAssertions;
using Shop.Application.Catalog;

namespace Shop.Application.Tests.Catalog;

public class ProductCursorTests
{
    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    [Theory]
    [InlineData(ProductSortField.Name, "Zebra \"quoted\" ü")]
    [InlineData(ProductSortField.Price, "19.99")]
    [InlineData(ProductSortField.CreatedAt, "1767268800123")]
    public void Round_trips(ProductSortField field, string key)
    {
        var original = new ProductCursor(field, SortDirection.Desc, key, Guid.NewGuid());

        ProductCursor.TryDecode(original.Encode(), out var decoded).Should().BeTrue();

        decoded.Should().Be(original);
    }

    [Fact]
    public void Encoded_cursor_is_valid_base64()
    {
        var act = () => Convert.FromBase64String(new ProductCursor(ProductSortField.Name, SortDirection.Asc, "a", Guid.NewGuid()).Encode());

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("%%%not base64")]
    [InlineData("bm90IGpzb24=")]
    public void Rejects_garbage(string? value) =>
        ProductCursor.TryDecode(value, out var cursor).Should().BeFalse();

    [Fact]
    public void Rejects_unknown_sort_field_and_unparseable_keys()
    {
        var id = Guid.NewGuid();
        ProductCursor.TryDecode(B64($$"""{"F":9,"D":0,"K":"a","I":"{{id}}"}"""), out _).Should().BeFalse();
        ProductCursor.TryDecode(B64($$"""{"F":1,"D":0,"K":"abc","I":"{{id}}"}"""), out _).Should().BeFalse();
        ProductCursor.TryDecode(B64($$"""{"F":2,"D":0,"K":"1.5","I":"{{id}}"}"""), out _).Should().BeFalse();
    }
}
