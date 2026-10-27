using GreenDonut;
using Shop.Application.Abstractions;
using Shop.Application.Catalog;
using Shop.Application.Catalog.Queries;

namespace Shop.Api.GraphQL.Catalog;

/// <summary>
/// Dedupes and caches <c>product(id:)</c> lookups within a request and runs each distinct id through the
/// application layer (<see cref="GetProductByIdQuery"/>), keeping the flow resolver -> dispatcher -> handler -> read service.
/// </summary>
public sealed class ProductByIdDataLoader(
    IDispatcher dispatcher,
    IBatchScheduler scheduler,
    DataLoaderOptions options)
    : BatchDataLoader<Guid, ProductDto>(scheduler, options)
{
    protected override async Task<IReadOnlyDictionary<Guid, ProductDto>> LoadBatchAsync(
        IReadOnlyList<Guid> keys,
        CancellationToken ct)
    {
        var results = await Task.WhenAll(keys.Select(async id =>
            (Id: id, Product: (await dispatcher.Send(new GetProductByIdQuery(id), ct)).Value)));
        return results.Where(r => r.Product is not null).ToDictionary(r => r.Id, r => r.Product!);
    }
}
