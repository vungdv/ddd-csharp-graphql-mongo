using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shop.Api.Observability;

namespace Shop.Api.IntegrationTests.Hardening;

public class TraceJsonConsoleFormatterTests
{
    private static JsonElement Format(Action<ILogger> log, string? category = "Shop.Test")
    {
        var sut = new TraceJsonConsoleFormatter();
        var writer = new StringWriter();
        var provider = new FormattingProvider(sut, writer) { Scopes = new LoggerExternalScopeProvider() };
        using var factory = LoggerFactory.Create(b => b.AddProvider(provider));
        log(factory.CreateLogger(category!));
        var line = writer.ToString().Trim();
        line.Should().NotContain("\n", "one JSON object per line");
        return JsonDocument.Parse(line).RootElement.Clone();
    }

    private sealed class FormattingProvider(TraceJsonConsoleFormatter formatter, StringWriter writer) : ILoggerProvider
    {
        public LoggerExternalScopeProvider Scopes { get; init; } = new();

        public ILogger CreateLogger(string categoryName) => new FormattingLogger(categoryName, formatter, writer, Scopes);

        public void Dispose()
        {
        }
    }

    private sealed class FormattingLogger(string category, TraceJsonConsoleFormatter formatter, StringWriter writer, LoggerExternalScopeProvider scopes) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatterFn) =>
            formatter.Write(new LogEntry<TState>(logLevel, category, eventId, state, exception, formatterFn), scopes, writer);
    }

    [Fact]
    public void Entries_inside_an_activity_carry_trace_id_and_span_id()
    {
        using var activity = new Activity("test").Start();

        var json = Format(l => l.LogInformation("hello {Name}", "world"));

        json.GetProperty("trace_id").GetString().Should().Be(activity.TraceId.ToString());
        json.GetProperty("span_id").GetString().Should().Be(activity.SpanId.ToString());
        json.GetProperty("message").GetString().Should().Be("hello world");
        json.GetProperty("level").GetString().Should().Be("Information");
        json.GetProperty("state").GetProperty("Name").GetString().Should().Be("world");
    }

    [Fact]
    public void Entries_outside_an_activity_have_no_correlation_fields_and_exceptions_are_included()
    {
        Activity.Current = null;

        var json = Format(l => l.LogError(new InvalidOperationException("boom"), "failed"));

        json.TryGetProperty("trace_id", out _).Should().BeFalse();
        json.GetProperty("exception").GetString().Should().Contain("boom");
    }

    [Fact]
    public void Active_scopes_are_emitted_as_a_scopes_array()
    {
        var json = Format(l =>
        {
            using var _ = l.BeginScope(new Dictionary<string, object?> { ["OrderId"] = "o-1" });
            using var __ = l.BeginScope("plain scope");
            l.LogInformation("inside");
        });

        var scopes = json.GetProperty("scopes");
        scopes.GetArrayLength().Should().Be(2);
        scopes[0].GetProperty("OrderId").GetString().Should().Be("o-1");
        scopes[1].GetString().Should().Be("plain scope");
    }
}
