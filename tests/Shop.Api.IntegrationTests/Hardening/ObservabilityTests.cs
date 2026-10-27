using Microsoft.AspNetCore.Hosting;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shop.Api.IntegrationTests.Catalog;
using Shop.Api.IntegrationTests.Mongo;
using Shop.Application.Catalog;

namespace Shop.Api.IntegrationTests.Hardening;

/// <summary>AGENTS.md Principle 3: W3C trace context, error/trace correlation, spans and metrics.</summary>
[Collection(MongoCollection.Name)]
public class ObservabilityTests(MongoFixture mongo)
{
    private const string Explode = """{ productBySku(sku: "X-1") { id } }""";

    private static void UseExplodingReadService(IServiceCollection services)
    {
        services.Remove(services.Single(d => d.ServiceType == typeof(IProductReadService)));
        services.AddSingleton<IProductReadService, ExplodingReadService>();
    }

    private static async Task<JsonElement> PostWithTraceparent(ApiHost api, string query, string traceId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql") { Content = JsonContent.Create(new { query }) };
        request.Headers.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");
        var response = await api.Client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [DockerFact]
    public async Task Error_trace_id_equals_the_propagated_W3C_trace_id()
    {
        using var api = new ApiHost(mongo, UseExplodingReadService);
        var traceId = ActivityTraceId.CreateRandom().ToString();

        var res = await PostWithTraceparent(api, Explode, traceId);

        res.GetProperty("errors")[0].GetProperty("extensions").GetProperty("traceId").GetString().Should().Be(traceId);
    }

    [DockerFact]
    public async Task Error_without_incoming_traceparent_still_carries_a_valid_activity_trace_id()
    {
        using var api = new ApiHost(mongo, UseExplodingReadService);

        var res = await api.Post(Explode);

        var traceId = res.GetProperty("errors")[0].GetProperty("extensions").GetProperty("traceId").GetString()!;
        traceId.Should().MatchRegex("^[0-9a-f]{32}$").And.NotBe(new string('0', 32));
    }

    [DockerFact]
    public async Task Unexpected_error_marks_the_active_span_failed_and_records_the_exception()
    {
        var captured = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = captured.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var api = new ApiHost(mongo, UseExplodingReadService);
        var traceId = ActivityTraceId.CreateRandom().ToString();

        await PostWithTraceparent(api, Explode, traceId);

        var failed = await Eventually(() => captured
            .Where(a => a.TraceId.ToString() == traceId && a.Status == ActivityStatusCode.Error)
            .Where(a => a.Events.Any(e => e.Name == "exception"))
            .ToList(), l => l.Any(a => a.Kind == ActivityKind.Server));
        failed.Where(a => a.Kind == ActivityKind.Server).Select(a => a.StatusDescription)
            .Should().Contain(typeof(InvalidOperationException).FullName, "the status description is the exception type, not its message");
        failed.Select(a => a.StatusDescription).Should().NotContain(d => d != null && d.Contains("secret"));
        failed.Should().NotBeEmpty("the unexpected-error filter records the exception on the active span");
        failed.SelectMany(a => a.Events).First(e => e.Name == "exception").Tags
            .Should().Contain(t => t.Key == "exception.type" && (string?)t.Value == typeof(InvalidOperationException).FullName);
    }

    [DockerFact]
    public async Task Request_and_unexpected_error_metrics_are_emitted()
    {
        var measurements = new ConcurrentBag<(string Instrument, double Value)>();
        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Name is "http.server.request.duration" or "shop.graphql.unexpected_errors")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        meters.SetMeasurementEventCallback<double>((i, v, _, _) => measurements.Add((i.Name, v)));
        meters.SetMeasurementEventCallback<long>((i, v, _, _) => measurements.Add((i.Name, v)));
        meters.Start();
        using var api = new ApiHost(mongo, UseExplodingReadService);

        await api.Post(Explode);
        meters.RecordObservableInstruments();

        await Eventually(() => measurements.ToList(), l =>
            l.Any(m => m.Instrument == "shop.graphql.unexpected_errors" && m.Value >= 1)
            && l.Any(m => m.Instrument == "http.server.request.duration" && m.Value >= 0));
        measurements.Should().Contain(m => m.Instrument == "shop.graphql.unexpected_errors" && m.Value >= 1);
        measurements.Should().Contain(m => m.Instrument == "http.server.request.duration" && m.Value >= 0);
    }

    [DockerFact]
    public async Task Mongo_and_graphql_spans_are_part_of_the_request_trace()
    {
        var sources = new ConcurrentBag<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.TraceId.ToString() == _traceId)
                {
                    sources.Add(a.Source.Name);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        using var api = new ApiHost(mongo);
        _traceId = ActivityTraceId.CreateRandom().ToString();

        await PostWithTraceparent(api, "{ products(first: 1) { totalCount } }", _traceId);

        await Eventually(() => sources.ToList(), l =>
            l.Contains("Microsoft.AspNetCore") && l.Any(x => x.StartsWith("HotChocolate", StringComparison.Ordinal)) && l.Any(x => x.StartsWith("MongoDB", StringComparison.Ordinal)));
        sources.Should().Contain("Microsoft.AspNetCore");
        sources.Should().Contain(s => s.StartsWith("HotChocolate", StringComparison.Ordinal));
        sources.Should().Contain(s => s.StartsWith("MongoDB", StringComparison.Ordinal));
    }

    /// <summary>Polls up to ~2s for asynchronous telemetry callbacks (activity stop / metric flush) to arrive.</summary>
    private static async Task<T> Eventually<T>(Func<T> read, Func<T, bool> done)
    {
        var value = read();
        for (var i = 0; i < 40 && !done(value); i++)
        {
            await Task.Delay(50);
            value = read();
        }

        return value;
    }

    private string _traceId = string.Empty;

    [DockerFact]
    public async Task Host_serves_requests_when_an_otlp_endpoint_is_configured_even_if_it_is_unreachable()
    {
        var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("Mongo:ConnectionString", mongo.ConnectionString);
            b.UseSetting("Mongo:Database", "shop_" + Guid.NewGuid().ToString("N"));
            b.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:1");
        });
        using var _ = factory;
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/graphql", new { query = "{ products(first: 1) { totalCount } }" });

        response.EnsureSuccessStatusCode();
    }

    private sealed class ExplodingReadService : IProductReadService
    {
        public Task<ProductDto?> GetById(Guid id, CancellationToken ct = default) => throw Boom();

        public Task<ProductDto?> GetBySku(string sku, CancellationToken ct = default) => throw Boom();

        public Task<IReadOnlyList<ProductDto>> GetByIds(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => throw Boom();

        public Task<ProductPageSlice> List(ProductPageRequest request, CancellationToken ct = default) => throw Boom();

        private static InvalidOperationException Boom() => new("secret connection detail");
    }
}
