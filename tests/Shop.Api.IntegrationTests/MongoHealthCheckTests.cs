using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;
using Shop.Infrastructure.Mongo;

namespace Shop.Api.IntegrationTests;

public class MongoHealthCheckTests
{
    private class ThrowingDatabase : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new MongoConnectionException(default, "unreachable");
    }

    [Fact]
    public async Task Reports_unhealthy_when_database_throws()
    {
        var db = DispatchProxy.Create<IMongoDatabase, ThrowingDatabase>();
        var check = new MongoHealthCheck(db);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
