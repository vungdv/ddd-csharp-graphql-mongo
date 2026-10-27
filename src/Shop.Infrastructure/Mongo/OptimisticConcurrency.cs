using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Shop.Domain.SharedKernel;

namespace Shop.Infrastructure.Mongo;

/// <summary>
/// Optimistic-concurrency convention shared by aggregate repositories (spec 6): replace where
/// <c>{ _id, version: expected }</c> and store <c>version + 1</c>; zero matches -> CONCURRENCY_CONFLICT.
/// </summary>
internal static class OptimisticConcurrency
{
    public static async Task Replace<TAggregate, TId>(
        IMongoCollection<TAggregate> collection,
        IClientSessionHandle? session,
        TAggregate aggregate,
        string aggregateName,
        Func<MongoWriteException, DomainException?> mapWriteError,
        CancellationToken ct)
        where TAggregate : AggregateRoot<TId>
        where TId : notnull
    {
        var expected = aggregate.Version;
        var setVersion = BsonClassMap.LookupClassMap(typeof(TAggregate)).AllMemberMaps
            .Single(m => m.MemberName == nameof(AggregateRoot<TId>.Version)).Setter;

        // The replacement document carries the incremented version.
        setVersion(aggregate, expected + 1);
        try
        {
            System.Linq.Expressions.Expression<Func<TAggregate, bool>> filter =
                a => a.Id.Equals(aggregate.Id) && a.Version == expected;
            var result = session is null
                ? await collection.ReplaceOneAsync(filter, aggregate, cancellationToken: ct)
                : await collection.ReplaceOneAsync(session, filter, aggregate, cancellationToken: ct);

            if (result.MatchedCount == 0)
            {
                setVersion(aggregate, expected);
                throw new DomainException(
                    DomainErrorCodes.ConcurrencyConflict,
                    $"{aggregateName} {aggregate.Id} was modified concurrently or no longer exists.");
            }
        }
        catch (MongoWriteException ex) when (mapWriteError(ex) is { } mapped)
        {
            setVersion(aggregate, expected);
            throw mapped;
        }
        catch (Exception) when (aggregate.Version != expected)
        {
            setVersion(aggregate, expected);
            throw;
        }
    }
}
