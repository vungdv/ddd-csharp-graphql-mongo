using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Shop.Application.Abstractions;
using Shop.Application.Catalog;
using Shop.Application.Ordering;
using Shop.Domain.Catalog;
using Shop.Domain.Ordering;
using Shop.Domain.SharedKernel;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Mongo;
using Shop.Infrastructure.Ordering;

namespace Shop.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.SectionName));

        services.AddSingleton<IMongoClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(3);
            return new MongoClient(settings);
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            return sp.GetRequiredService<IMongoClient>().GetDatabase(options.Database);
        });

        MongoMappings.Register();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<MongoUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MongoUnitOfWork>());
        services.AddScoped<IMongoSessionProvider>(sp => sp.GetRequiredService<MongoUnitOfWork>());
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddSingleton<IProductReadService, ProductReadService>();
        services.AddSingleton<IOrderReadService, OrderReadService>();
        services.AddSingleton<MongoIndexReadiness>();
        services.AddHostedService<MongoIndexInitializer>();

        services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongo")
            .AddCheck<MongoIndexesHealthCheck>("mongo-indexes");

        return services;
    }

    /// <summary>Registers the idempotent sample-data seeder; call only in Development.</summary>
    public static IServiceCollection AddCatalogSeeding(this IServiceCollection services) =>
        services.AddHostedService<CatalogSeeder>();
}
