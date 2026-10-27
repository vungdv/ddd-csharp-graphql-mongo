using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using Shop.Domain.Catalog;
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
            BsonSerializer.TryRegisterSerializer(new SkuSerializer());

            BsonClassMap.TryRegisterClassMap<Money>(cm =>
            {
                cm.MapProperty(m => m.Amount)
                    .SetElementName("amount")
                    .SetSerializer(new DecimalSerializer(BsonType.Decimal128));
                cm.MapProperty(m => m.Currency).SetElementName("currency");
                var ctor = typeof(Money).GetConstructor(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    [typeof(decimal), typeof(string)])!;
                cm.MapConstructor(ctor, nameof(Money.Amount), nameof(Money.Currency));
            });

            // Inherited members must be mapped on the class that declares them.
            BsonClassMap.TryRegisterClassMap<Entity<ProductId>>(cm =>
                cm.MapIdProperty(e => e.Id).SetElementName("_id"));
            BsonClassMap.TryRegisterClassMap<AggregateRoot<ProductId>>(cm =>
                cm.MapProperty(a => a.Version).SetElementName("version"));

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

            _registered = true;
        }
    }
}
