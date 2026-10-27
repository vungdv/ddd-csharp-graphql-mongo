using Shop.Application.Abstractions;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Behaviors;

/// <summary>Maps expected business-rule violations (<see cref="DomainException"/>) to failed results. Others propagate.</summary>
public sealed class DomainExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<Result<TResponse>> Handle(TRequest request, Func<Task<Result<TResponse>>> next, CancellationToken ct)
    {
        try
        {
            return await next();
        }
        catch (DomainException ex)
        {
            return Result<TResponse>.Failure(new AppError(ex.Code, ex.Message, ex.Field));
        }
    }
}
