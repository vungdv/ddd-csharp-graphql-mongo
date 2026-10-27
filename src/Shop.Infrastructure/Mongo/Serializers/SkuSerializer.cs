using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Shop.Domain.Catalog;

namespace Shop.Infrastructure.Mongo.Serializers;

internal sealed class SkuSerializer : SerializerBase<Sku>
{
    public override Sku Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args) =>
        Sku.Create(context.Reader.ReadString());

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Sku value) =>
        context.Writer.WriteString(value.Value);
}
