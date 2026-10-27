using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Shop.Domain.Ordering;

namespace Shop.Infrastructure.Mongo.Serializers;

/// <summary>Stores <see cref="OrderId"/> as a standard-representation Guid (BSON binary subtype 4).</summary>
internal sealed class OrderIdSerializer : SerializerBase<OrderId>
{
    private static readonly GuidSerializer Guids = new(GuidRepresentation.Standard);

    public override OrderId Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
        new(Guids.Deserialize(context));

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, OrderId value) =>
        Guids.Serialize(context, value.Value);
}
