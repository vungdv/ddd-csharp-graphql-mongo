namespace Shop.Infrastructure.Mongo;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "mongodb://localhost:27017/?replicaSet=rs0&directConnection=true";

    public string Database { get; set; } = "shop";
}
