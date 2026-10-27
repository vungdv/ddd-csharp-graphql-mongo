using System.Globalization;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using Shop.Application.Catalog;
using SortDirection = Shop.Application.Catalog.SortDirection;

namespace Shop.Infrastructure.Catalog;

/// <summary>CQRS read side: queries the products collection and projects raw documents straight into DTOs.</summary>
public sealed class ProductReadService : IProductReadService
{
    private readonly IMongoCollection<BsonDocument> _products;

    public ProductReadService(IMongoDatabase database) =>
        _products = database.GetCollection<BsonDocument>(ProductRepository.CollectionName);

    /// <summary>
    /// Case-insensitive (strength Secondary) English collation for list queries: gives natural name ordering and keeps
    /// keyset comparisons consistent with the sort. Side effect: the exact-match category filter is case-insensitive too.
    /// </summary>
    internal static readonly Collation NaturalOrder = new("en", strength: CollationStrength.Secondary);

    private static readonly FilterDefinitionBuilder<BsonDocument> F = Builders<BsonDocument>.Filter;

    public async Task<ProductDto?> GetById(Guid id, CancellationToken ct = default)
    {
        var doc = await _products.Find(F.Eq("_id", ToBson(id))).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToDto(doc);
    }

    public async Task<ProductDto?> GetBySku(string sku, CancellationToken ct = default)
    {
        var doc = await _products.Find(F.Eq("sku", sku)).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToDto(doc);
    }

    public async Task<IReadOnlyList<ProductDto>> GetByIds(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var docs = await _products.Find(F.In("_id", ids.Select(ToBson))).ToListAsync(ct);
        return docs.Select(ToDto).ToList();
    }

    public async Task<ProductPageSlice> List(ProductPageRequest request, CancellationToken ct = default)
    {
        var filter = BuildFilter(request.Filter);
        var total = await _products.CountDocumentsAsync(
            filter,
            new CountOptions { Collation = NaturalOrder },
            ct);

        var path = SortPath(request.Sort.Field);
        var descending = request.Sort.Direction == SortDirection.Desc;

        var pageFilter = request.After is null
            ? filter
            : F.And(filter, AfterCursor(request.After, path, descending));

        var sort = descending
            ? Builders<BsonDocument>.Sort.Descending(path).Descending("_id")
            : Builders<BsonDocument>.Sort.Ascending(path).Ascending("_id");

        var docs = await _products.Find(pageFilter, new FindOptions { Collation = NaturalOrder })
            .Sort(sort).Limit(request.Take).ToListAsync(ct);
        return new ProductPageSlice(docs.Select(ToDto).ToList(), (int)total);
    }

    private static FilterDefinition<BsonDocument> BuildFilter(ProductFilter f)
    {
        var parts = new List<FilterDefinition<BsonDocument>>();
        if (!string.IsNullOrWhiteSpace(f.Category))
        {
            parts.Add(F.Eq("category", f.Category));
        }

        if (f.IsActive is { } active)
        {
            parts.Add(F.Eq("isActive", active));
        }

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(f.Search.Trim()), "i");
            parts.Add(F.Or(F.Regex("name", pattern), F.Regex("description", pattern)));
        }

        if (f.MinPrice is { } min)
        {
            parts.Add(F.Gte("price.amount", new Decimal128(min)));
        }

        if (f.MaxPrice is { } max)
        {
            parts.Add(F.Lte("price.amount", new Decimal128(max)));
        }

        return parts.Count == 0 ? F.Empty : F.And(parts);
    }

    /// <summary>Keyset condition: (key beyond cursor) OR (same key AND id beyond cursor), matching the (key, _id) sort.</summary>
    private static FilterDefinition<BsonDocument> AfterCursor(ProductCursor after, string path, bool descending)
    {
        var key = KeyToBson(after.Field, after.Key);
        var id = ToBson(after.Id);
        return descending
            ? F.Or(F.Lt(path, key), F.And(F.Eq(path, key), F.Lt("_id", id)))
            : F.Or(F.Gt(path, key), F.And(F.Eq(path, key), F.Gt("_id", id)));
    }

    private static string SortPath(ProductSortField field) => field switch
    {
        ProductSortField.Name => "name",
        ProductSortField.Price => "price.amount",
        ProductSortField.CreatedAt => "createdAt",
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    private static BsonValue KeyToBson(ProductSortField field, string key) => field switch
    {
        ProductSortField.Name => key,
        ProductSortField.Price => new Decimal128(decimal.Parse(key, NumberStyles.Number, CultureInfo.InvariantCulture)),
        _ => new BsonDateTime(long.Parse(key, NumberStyles.Integer, CultureInfo.InvariantCulture)),
    };

    private static BsonBinaryData ToBson(Guid id) => new(id, GuidRepresentation.Standard);

    private static ProductDto ToDto(BsonDocument d)
    {
        var price = d["price"].AsBsonDocument;
        return new ProductDto(
            d["_id"].AsGuid,
            d["sku"].AsString,
            d["name"].AsString,
            d.GetValue("description", BsonNull.Value) is { IsString: true } desc ? desc.AsString : null,
            new MoneyDto(Decimal128.ToDecimal(price["amount"].AsDecimal128), price["currency"].AsString),
            d["category"].AsString,
            d["stockQuantity"].AsInt32,
            d["isActive"].AsBoolean,
            ToUtc(d["createdAt"]),
            ToUtc(d["updatedAt"]));
    }

    private static DateTimeOffset ToUtc(BsonValue v) => new(v.ToUniversalTime(), TimeSpan.Zero);
}
