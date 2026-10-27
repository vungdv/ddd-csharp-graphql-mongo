using Polly;
using Polly.Retry;
using Shop.Domain.Ordering;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Resilience;

/// <summary>
/// Named retry policies (Polly) for expected optimistic-concurrency failures. Transient *transaction* errors
/// (write conflicts) are retried by the unit of work itself (driver transaction retry).
/// </summary>
public static class ResiliencePipelines
{
    public const int MaxRetries = 3;

    /// <summary>Retries the whole operation when a freshly generated order number/id collided with an existing order.</summary>
    public static ResiliencePipeline DuplicateOrderNumber { get; } = Build(new PredicateBuilder().Handle<DuplicateOrderException>());

    /// <summary>Retries a load-modify-save operation after an optimistic version mismatch (reload sees the newer state).</summary>
    public static ResiliencePipeline ConcurrencyConflict { get; } = Build(
        new PredicateBuilder().Handle<DomainException>(e => e.Code == DomainErrorCodes.ConcurrencyConflict));

    private static ResiliencePipeline Build(PredicateBuilder<object> predicate) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = predicate,
                MaxRetryAttempts = MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(10),
                UseJitter = true,
            })
            .Build();
}
