namespace Shop.Application.Abstractions;

/// <summary>Marker for commands and queries; <typeparamref name="TResponse"/> is wrapped in a <see cref="Result{T}"/>.</summary>
public interface IRequest<TResponse>;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<Result<TResponse>> Handle(TRequest request, CancellationToken ct);
}

/// <summary>Cross-cutting step around handlers (validation, error mapping). First registered runs outermost.</summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<Result<TResponse>> Handle(TRequest request, Func<Task<Result<TResponse>>> next, CancellationToken ct);
}

public interface IDispatcher
{
    Task<Result<TResponse>> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}
