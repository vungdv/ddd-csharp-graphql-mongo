using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Application.Abstractions;

/// <summary>Resolves the handler and pipeline behaviors for a request and runs them.</summary>
public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Invokers = new();

    public Task<Result<TResponse>> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoker = (Invoker<TResponse>)Invokers.GetOrAdd(
            request.GetType(),
            static type => Activator.CreateInstance(typeof(Invoker<,>).MakeGenericType(type, typeof(TResponse)))!);
        return invoker.Invoke(request, services, ct);
    }

    private abstract class Invoker<TResponse>
    {
        public abstract Task<Result<TResponse>> Invoke(IRequest<TResponse> request, IServiceProvider services, CancellationToken ct);
    }

    private sealed class Invoker<TRequest, TResponse> : Invoker<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<Result<TResponse>> Invoke(IRequest<TResponse> request, IServiceProvider services, CancellationToken ct)
        {
            var typed = (TRequest)request;
            var handler = services.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
            Func<Task<Result<TResponse>>> next = () => handler.Handle(typed, ct);
            foreach (var behavior in services.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
            {
                var inner = next;
                next = () => behavior.Handle(typed, inner, ct);
            }

            return next();
        }
    }
}
