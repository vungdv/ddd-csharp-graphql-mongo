using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions;
using Shop.Application.Behaviors;
using Shop.Application.Catalog.Integration;
using Shop.Application.Events;
using Shop.Application.Ordering;
using Shop.Domain.Ordering;

namespace Shop.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(AssemblyMarker).Assembly;

        services.AddScoped<IDispatcher, Dispatcher>();

        // Registration order is pipeline order: first registered is outermost.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(DomainExceptionBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        Type[] handlerInterfaces = [typeof(IRequestHandler<,>), typeof(IDomainEventHandler<>)];
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var service in type.GetInterfaces()
                         .Where(i => i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition())))
            {
                services.AddScoped(service, type);
            }
        }

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddSingleton<IOrderNumberGenerator>(RandomOrderNumberGenerator.Instance);

        // Catalog-side adapters for the ports Ordering depends on (spec 3.3).
        services.AddScoped<IProductCatalog, ProductCatalogAdapter>();
        services.AddScoped<IStockReservation, StockReservationAdapter>();

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Singleton, includeInternalTypes: true);
        return services;
    }
}
