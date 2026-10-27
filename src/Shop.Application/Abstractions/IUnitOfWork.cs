namespace Shop.Application.Abstractions;

/// <summary>
/// Atomic unit of work port (implemented with a database transaction). Everything the repositories write inside
/// <see cref="ExecuteAsync{T}"/> commits or rolls back together.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="work"/> in a transaction. The implementation may invoke <paramref name="work"/> more than once
    /// when the store reports a transient transaction error (e.g. a write conflict), so <paramref name="work"/> must
    /// reload everything it reads and must not have side effects outside the transaction. An exception thrown by
    /// <paramref name="work"/> rolls the transaction back and propagates. Nested calls join the running transaction.
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);
}
