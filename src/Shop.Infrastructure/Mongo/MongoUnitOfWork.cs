using MongoDB.Driver;
using Shop.Application.Abstractions;

namespace Shop.Infrastructure.Mongo;

/// <summary>Gives repositories the ambient transaction session, if a unit of work is running.</summary>
public interface IMongoSessionProvider
{
    IClientSessionHandle? Current { get; }
}

/// <summary>
/// Scoped unit of work over a MongoDB multi-document transaction (requires a replica set). Uses the driver's
/// <c>WithTransactionAsync</c>, which re-runs the callback on TransientTransactionError (e.g. WriteConflict) and retries
/// the commit on UnknownTransactionCommitResult, up to the driver's 120 s transaction time limit.
/// </summary>
public sealed class MongoUnitOfWork(IMongoClient client) : IUnitOfWork, IMongoSessionProvider
{
    private static readonly TransactionOptions Options = new(
        readConcern: ReadConcern.Snapshot,
        writeConcern: WriteConcern.WMajority);

    public IClientSessionHandle? Current { get; private set; }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        if (Current is not null)
        {
            return await work(ct); // join the running transaction
        }

        using var session = await client.StartSessionAsync(cancellationToken: ct);
        Current = session;
        try
        {
            return await session.WithTransactionAsync((_, token) => work(token), Options, ct);
        }
        finally
        {
            Current = null;
        }
    }
}
