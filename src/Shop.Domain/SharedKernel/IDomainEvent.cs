namespace Shop.Domain.SharedKernel;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
