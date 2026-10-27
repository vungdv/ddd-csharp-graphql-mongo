using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions;
using Shop.Application.Behaviors;

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

        var handlerInterface = typeof(IRequestHandler<,>);
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var service in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterface))
            {
                services.AddScoped(service, type);
            }
        }

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Singleton, includeInternalTypes: true);
        return services;
    }
}
