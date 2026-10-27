using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Shop.Api.Observability;

/// <summary>
/// One-JSON-object-per-line console formatter that stamps every entry with the active W3C <c>trace_id</c> and
/// <c>span_id</c> (from <see cref="Activity.Current"/>), so logs correlate with traces and with <c>extensions.traceId</c>.
/// </summary>
public sealed class TraceJsonConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "json-trace";

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null && logEntry.Exception is null)
        {
            return;
        }

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("timestamp", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            json.WriteString("level", logEntry.LogLevel.ToString());
            json.WriteString("category", logEntry.Category);
            json.WriteNumber("event_id", logEntry.EventId.Id);
            json.WriteString("message", message);
            if (logEntry.Exception is { } ex)
            {
                json.WriteString("exception", ex.ToString());
            }

            if (Activity.Current is { } activity)
            {
                json.WriteString("trace_id", activity.TraceId.ToString());
                json.WriteString("span_id", activity.SpanId.ToString());
            }

            if (logEntry.State is IEnumerable<KeyValuePair<string, object?>> state)
            {
                json.WriteStartObject("state");
                foreach (var (key, value) in state)
                {
                    if (key != "{OriginalFormat}")
                    {
                        json.WriteString(key, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                    }
                }

                json.WriteEndObject();
            }

            if (scopeProvider is not null)
            {
                json.WriteStartArray("scopes");
                scopeProvider.ForEachScope(
                    static (scope, w) =>
                    {
                        if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                        {
                            w.WriteStartObject();
                            foreach (var (k, v) in pairs)
                            {
                                w.WriteString(k, Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
                            }

                            w.WriteEndObject();
                        }
                        else
                        {
                            w.WriteStringValue(Convert.ToString(scope, System.Globalization.CultureInfo.InvariantCulture));
                        }
                    },
                    json);
                json.WriteEndArray();
            }

            json.WriteEndObject();
        }

        textWriter.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}
