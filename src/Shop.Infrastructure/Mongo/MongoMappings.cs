using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using Shop.Domain.Catalog;
using Shop.Domain.Ordering;
using Shop.Domain.SharedKernel;
using Shop.Infrastructure.Mongo.Serializers;

namespace Shop.Infrastructure.Mongo;

/// <summary>Persistence mapping kept out of the Domain: BsonClassMap registrations (no attributes on domain types).</summary>
public static class MongoMappings
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            ConventionRegistry.Register(
                "ShopConventions",
                new ConventionPack { new EnumRepresentationConvention(BsonType.String) },
                t => t.Namespace?.StartsWith("Shop.Domain", StringComparison.Ordinal) == true);

            BsonSerializer.TryRegisterSerializer(new ProductIdSerializer());
            BsonSerializer.TryRegisterSerializer(new OrderIdSerializer());
            BsonSerializer.TryRegisterSerializer(new SkuSerializer());

            BsonClassMap.TryRegisterClassMap<Money>(cm =>
            {
                cm.MapProperty(m => m.Amount)
                    .SetElementName("amount")
                    .SetSerializer(new DecimalSerializer(BsonType.Decimal128));
                cm.MapProperty(m => m.Currency).SetElementName("currency");
                MapPrivateConstructor(cm, [typeof(decimal), typeof(string)], nameof(Money.Amount), nameof(Money.Currency));
            });

            // Convention for every aggregate: Id -> _id, Version -> version.
            MapAggregate<ProductId>();
            MapAggregate<OrderId>();

            BsonClassMap.TryRegisterClassMap<Product>(cm =>
            {
                cm.MapProperty(p => p.Sku).SetElementName("sku");
                cm.MapProperty(p => p.Name).SetElementName("name");
                cm.MapProperty(p => p.Description).SetElementName("description");
                cm.MapProperty(p => p.Price).SetElementName("price");
                cm.MapProperty(p => p.Category).SetElementName("category");
                cm.MapProperty(p => p.StockQuantity).SetElementName("stockQuantity");
                cm.MapProperty(p => p.IsActive).SetElementName("isActive");
                cm.MapProperty(p => p.CreatedAt).SetElementName("createdAt")
                    .SetSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));
                cm.MapProperty(p => p.UpdatedAt).SetElementName("updatedAt")
                    .SetSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));
            });

            RegisterOrdering();

            _registered = true;
        }
    }

    /// <summary>
    /// Maps the <c>Entity&lt;TId&gt;</c> / <c>AggregateRoot&lt;TId&gt;</c> base members. Inherited members must be mapped
    /// on the class that declares them, so each id type needs its own registration.
    /// </summary>
    private static void MapAggregate<TId>()
        where TId : notnull
    {
        BsonClassMap.TryRegisterClassMap<Entity<TId>>(cm => cm.MapIdProperty(e => e.Id).SetElementName("_id"));
        BsonClassMap.TryRegisterClassMap<AggregateRoot<TId>>(cm => cm.MapProperty(a => a.Version).SetElementName("version"));
    }

    private static void MapPrivateConstructor<T>(BsonClassMap<T> cm, Type[] parameterTypes, params string[] memberNames)
    {
        var ctor = typeof(T).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            parameterTypes)!;
        cm.MapConstructor(ctor, memberNames);
    }

    private static void RegisterOrdering()
    {
        BsonClassMap.TryRegisterClassMap<CustomerInfo>(cm =>
        {
            cm.MapProperty(c => c.Name).SetElementName("name");
            cm.MapProperty(c => c.Email).SetElementName("email");
            MapPrivateConstructor(cm, [typeof(string), typeof(string)], nameof(CustomerInfo.Name), nameof(CustomerInfo.Email));
        });

        BsonClassMap.TryRegisterClassMap<Address>(cm =>
        {
            cm.MapProperty(a => a.Line1).SetElementName("line1");
            cm.MapProperty(a => a.Line2).SetElementName("line2");
            cm.MapProperty(a => a.City).SetElementName("city");
            cm.MapProperty(a => a.State).SetElementName("state");
            cm.MapProperty(a => a.PostalCode).SetElementName("postalCode");
            cm.MapProperty(a => a.Country).SetElementName("country");
            MapPrivateConstructor(
                cm,
                [typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string)],
                nameof(Address.Line1),
                nameof(Address.Line2),
                nameof(Address.City),
                nameof(Address.State),
                nameof(Address.PostalCode),
                nameof(Address.Country));
        });

        // Embedded line value: plain productId field (no _id). lineTotal is derived and persisted for read models only.
        BsonClassMap.TryRegisterClassMap<OrderLine>(cm =>
        {
            cm.SetIgnoreExtraElements(true);
            cm.MapProperty(l => l.ProductId).SetElementName("productId");
            cm.MapProperty(l => l.Sku).SetElementName("sku");
            cm.MapProperty(l => l.ProductName).SetElementName("productName");
            cm.MapProperty(l => l.UnitPrice).SetElementName("unitPrice");
            cm.MapProperty(l => l.Quantity).SetElementName("quantity");
            cm.MapProperty(l => l.LineTotal).SetElementName("lineTotal");
        });

        BsonClassMap.TryRegisterClassMap<Order>(cm =>
        {
            cm.SetIgnoreExtraElements(true);
            cm.MapProperty(o => o.OrderNumber).SetElementName("orderNumber");
            cm.MapProperty(o => o.Customer).SetElementName("customer");
            cm.MapProperty(o => o.ShippingAddress).SetElementName("shippingAddress");
            cm.MapField("_lines").SetElementName("lines");
            cm.MapProperty(o => o.Status).SetElementName("status")
                .SetSerializer(new EnumSerializer<OrderStatus>(BsonType.String));
            cm.MapProperty(o => o.Total).SetElementName("total");
            cm.MapProperty(o => o.PlacedAt).SetElementName("placedAt")
                .SetSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));
            cm.MapProperty(o => o.CancelledAt).SetElementName("cancelledAt")
                .SetSerializer(new NullableSerializer<DateTimeOffset>(new DateTimeOffsetSerializer(BsonType.DateTime)));
            cm.MapProperty(o => o.CancellationReason).SetElementName("cancellationReason");
        });
    }
}
