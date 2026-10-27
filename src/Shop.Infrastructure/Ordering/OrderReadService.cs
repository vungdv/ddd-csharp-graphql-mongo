using MongoDB.Bson;
using MongoDB.Driver;
using Shop.Application.Catalog;
using Shop.Application.Ordering;
using Shop.Domain.Ordering;

namespace Shop.Infrastructure.Ordering;

/// <summary>CQRS read side: queries the orders collection and projects raw documents straight into DTOs.</summary>
public sealed class OrderReadService : IOrderReadService
{
    private static readonly FilterDefinitionBuilder<BsonDocument> F = Builders<BsonDocument>.Filter;

    private readonly IMongoCollection<BsonDocument> _orders;

    public OrderReadService(IMongoDatabase database) =>
        _orders = database.GetCollection<BsonDocument>(OrderRepository.CollectionName);

    public async Task<OrderDto?> GetById(Guid id, CancellationToken ct = default)
    {
        var doc = await _orders.Find(F.Eq("_id", ToBson(id))).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToDto(doc);
    }

    public async Task<OrderDto?> GetByOrderNumber(string orderNumber, CancellationToken ct = default)
    {
        var doc = await _orders.Find(F.Eq("orderNumber", orderNumber)).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToDto(doc);
    }

    public async Task<OrderPageSlice> List(OrderPageRequest request, CancellationToken ct = default)
    {
        var filter = BuildFilter(request.Filter);
        var total = await _orders.CountDocumentsAsync(filter, cancellationToken: ct);

        var pageFilter = request.After is null ? filter : F.And(filter, AfterCursor(request.After));
        var docs = await _orders.Find(pageFilter)
            .Sort(Builders<BsonDocument>.Sort.Descending("placedAt").Descending("_id"))
            .Limit(request.Take)
            .ToListAsync(ct);
        return new OrderPageSlice(docs.Select(ToDto).ToList(), (int)total);
    }

    private static FilterDefinition<BsonDocument> BuildFilter(OrderFilter f)
    {
        var parts = new List<FilterDefinition<BsonDocument>>();
        if (f.Status is { } status)
        {
            parts.Add(F.Eq("status", status.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(f.CustomerEmail))
        {
            parts.Add(F.Eq("customer.email", f.CustomerEmail.Trim().ToLowerInvariant()));
        }

        if (f.PlacedFrom is { } from)
        {
            parts.Add(F.Gte("placedAt", new BsonDateTime(from.UtcDateTime)));
        }

        if (f.PlacedTo is { } to)
        {
            parts.Add(F.Lte("placedAt", new BsonDateTime(to.UtcDateTime)));
        }

        return parts.Count == 0 ? F.Empty : F.And(parts);
    }

    /// <summary>Keyset condition for the (placedAt desc, _id desc) sort.</summary>
    private static FilterDefinition<BsonDocument> AfterCursor(OrderCursor after)
    {
        var key = new BsonDateTime(after.PlacedAtMs);
        return F.Or(F.Lt("placedAt", key), F.And(F.Eq("placedAt", key), F.Lt("_id", ToBson(after.Id))));
    }

    private static BsonBinaryData ToBson(Guid id) => new(id, GuidRepresentation.Standard);

    private static OrderDto ToDto(BsonDocument d)
    {
        var customer = d["customer"].AsBsonDocument;
        var address = d["shippingAddress"].AsBsonDocument;
        return new OrderDto(
            d["_id"].AsGuid,
            d["orderNumber"].AsString,
            new CustomerDto(customer["name"].AsString, customer["email"].AsString),
            new AddressDto(
                address["line1"].AsString,
                OptionalString(address, "line2"),
                address["city"].AsString,
                OptionalString(address, "state"),
                address["postalCode"].AsString,
                address["country"].AsString),
            d["lines"].AsBsonArray.Select(v => ToLine(v.AsBsonDocument)).ToList(),
            Enum.Parse<OrderStatus>(d["status"].AsString),
            ToMoney(d["total"]),
            ToUtc(d["placedAt"]),
            d.GetValue("cancelledAt", BsonNull.Value) is { IsBsonDateTime: true } c ? ToUtc(c) : null,
            OptionalString(d, "cancellationReason"));
    }

    private static OrderLineDto ToLine(BsonDocument l)
    {
        var unit = ToMoney(l["unitPrice"]);
        return new OrderLineDto(
            l["productId"].AsGuid,
            l["sku"].AsString,
            l["productName"].AsString,
            unit,
            l["quantity"].AsInt32,
            l.Contains("lineTotal") ? ToMoney(l["lineTotal"]) : unit with { Amount = unit.Amount * l["quantity"].AsInt32 });
    }

    private static MoneyDto ToMoney(BsonValue v)
    {
        var m = v.AsBsonDocument;
        return new MoneyDto(Decimal128.ToDecimal(m["amount"].AsDecimal128), m["currency"].AsString);
    }

    private static string? OptionalString(BsonDocument d, string name) =>
        d.GetValue(name, BsonNull.Value) is { IsString: true } s ? s.AsString : null;

    private static DateTimeOffset ToUtc(BsonValue v) => new(v.ToUniversalTime(), TimeSpan.Zero);
}
