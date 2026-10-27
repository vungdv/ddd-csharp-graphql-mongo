using System.Diagnostics;
using MongoDB.Driver;
using Shop.Infrastructure.Mongo;
using Testcontainers.MongoDb;

namespace Shop.Api.IntegrationTests.Mongo;

/// <summary>Skips tests (instead of failing) when no Docker daemon is reachable.</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> DockerAvailable = new(Probe);

    public static bool RequireDocker => Environment.GetEnvironmentVariable("REQUIRE_DOCKER") == "1";

    public static bool IsDockerAvailable => DockerAvailable.Value;

    public DockerFactAttribute()
    {
        if (!DockerAvailable.Value)
        {
            if (RequireDocker)
            {
                throw new InvalidOperationException("REQUIRE_DOCKER=1 is set but Docker is not available.");
            }

            Skip = "Docker is not available; Testcontainers MongoDB tests were skipped.";
        }
    }

    private static bool Probe()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("docker", "info --format {{.ServerVersion}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p is null)
            {
                return false;
            }

            if (!p.WaitForExit(15_000))
            {
                p.Kill();
                return false;
            }

            return p.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public sealed class MongoFixture : IAsyncLifetime
{
    private MongoDbContainer? _container;

    public IMongoClient Client { get; private set; } = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!DockerFactAttribute.IsDockerAvailable)
        {
            if (DockerFactAttribute.RequireDocker)
            {
                throw new InvalidOperationException("REQUIRE_DOCKER=1 is set but Docker is not available.");
            }

            return; // [DockerFact] tests are skipped and never touch Client.
        }

        _container = new MongoDbBuilder("mongo:7").WithReplicaSet().Build();
        await _container.StartAsync();
        MongoMappings.Register();
        ConnectionString = _container.GetConnectionString();
        Client = new MongoClient(ConnectionString);
    }

    public IMongoDatabase NewDatabase() => Client.GetDatabase("shop_" + Guid.NewGuid().ToString("N"));

    public Task DisposeAsync() => _container?.DisposeAsync().AsTask() ?? Task.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>
{
    public const string Name = "mongo";
}
