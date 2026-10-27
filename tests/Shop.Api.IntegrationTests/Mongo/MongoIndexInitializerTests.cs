using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Shop.Infrastructure.Mongo;

namespace Shop.Api.IntegrationTests.Mongo;

public class MongoIndexInitializerTests
{
    private sealed class CapturingLogger : ILogger
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Levels)
            {
                Levels.Add(logLevel);
            }
        }
    }

    private static Task<HealthCheckResult> Check(MongoIndexReadiness r) =>
        new MongoIndexesHealthCheck(r).CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task Becomes_ready_synchronously_when_first_attempt_succeeds()
    {
        var readiness = new MongoIndexReadiness();
        var sut = new MongoIndexInitializer(_ => Task.CompletedTask, readiness, null, TimeSpan.FromMilliseconds(10));

        await sut.StartAsync(CancellationToken.None);

        readiness.IsReady.Should().BeTrue();
        (await Check(readiness)).Status.Should().Be(HealthStatus.Healthy);
        await sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Startup_does_not_throw_when_failing_then_retries_until_ready_and_logs_error()
    {
        var readiness = new MongoIndexReadiness();
        var logger = new CapturingLogger();
        var calls = 0;
        var sut = new MongoIndexInitializer(
            _ => ++calls <= 4 ? throw new InvalidOperationException("mongo down") : Task.CompletedTask,
            readiness,
            logger,
            TimeSpan.FromMilliseconds(5));

        await sut.StartAsync(CancellationToken.None);

        readiness.IsReady.Should().BeFalse();
        (await Check(readiness)).Status.Should().Be(HealthStatus.Unhealthy);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!readiness.IsReady && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        readiness.IsReady.Should().BeTrue();
        calls.Should().Be(5);
        (await Check(readiness)).Status.Should().Be(HealthStatus.Healthy);
        lock (logger.Levels)
        {
            logger.Levels.Should().Contain(LogLevel.Warning).And.Contain(LogLevel.Error);
        }

        await sut.StopAsync(CancellationToken.None);
    }
}
