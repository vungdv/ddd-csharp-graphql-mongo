using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Shop.Domain.Catalog;

namespace Shop.Infrastructure.Mongo.Serializers;

/// <summary>Stores <see cref="ProductId"/> as a standard-representation Guid (BSON binary subtype 4).</summary>
internal sealed class ProductIdSerializer : SerializerBase<ProductId>
{
    private static readonly GuidSerializer Guids = new(GuidRepresentation.Standard);

    public override ProductId Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
        new(Guids.Deserialize(context));

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, ProductId value) =>
        Guids.Serialize(context, value.Value);
}
