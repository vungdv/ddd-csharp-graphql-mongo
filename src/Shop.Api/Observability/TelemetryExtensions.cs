using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Shop.Api.Observability;

/// <summary>Application-level telemetry instruments (meter and activity source names are stable contracts).</summary>
public static class ShopTelemetry
{
    public const string Name = "Shop.Api";

    public static readonly Meter Meter = new(Name);

    public static readonly Counter<long> UnexpectedErrors =
        Meter.CreateCounter<long>("shop.graphql.unexpected_errors", description: "GraphQL field errors masked as INTERNAL_ERROR.");
}

public static class TelemetryExtensions
{
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>JSON console logs carrying trace_id/span_id.</summary>
    public static ILoggingBuilder AddTraceJsonConsole(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddConsole(o => o.FormatterName = TraceJsonConsoleFormatter.FormatterName);
        logging.AddConsoleFormatter<TraceJsonConsoleFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>();
        return logging;
    }

    /// <summary>
    /// Registers OpenTelemetry tracing and metrics (ASP.NET Core, HotChocolate, MongoDB driver, app meter).
    /// Telemetry is exported over OTLP only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is configured.
    /// </summary>
    public static IServiceCollection AddShopTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var exportEnabled = !string.IsNullOrWhiteSpace(configuration[OtlpEndpointKey]);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(configuration["OTEL_SERVICE_NAME"] ?? "shop-api"))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation()
                    .AddSource("HotChocolate.Diagnostics")
                    .AddSource("MongoDB.Driver*");
                if (exportEnabled)
                {
                    t.AddOtlpExporter();
                }
            })
            .WithMetrics(m =>
            {
                // http.server.request.duration (latency histogram, count = invocations, error.type = errors).
                m.AddAspNetCoreInstrumentation().AddMeter(ShopTelemetry.Name);
                if (exportEnabled)
                {
                    m.AddOtlpExporter();
                }
            });
        return services;
    }

    /// <summary>
    /// Marks the active span and the enclosing server (request) span as failed and records the exception on them.
    /// The status description is the exception type, never its message. GraphQL answers 200 for field errors, so the
    /// request span would otherwise look successful; the resolver span's status can also be overwritten by the engine.
    /// </summary>
    public static void RecordFailure(this Activity? activity, Exception exception)
    {
        var description = exception.GetType().FullName;
        for (var current = activity; current is not null; current = current.Parent)
        {
            current.AddException(exception);
            current.SetStatus(ActivityStatusCode.Error, description);
            if (current.Kind == ActivityKind.Server)
            {
                break;
            }
        }
    }
}
