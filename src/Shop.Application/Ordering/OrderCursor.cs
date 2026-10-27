using System.Text;
using System.Text.Json;

namespace Shop.Application.Ordering;

/// <summary>
/// Opaque pagination cursor for orders (same scheme as <c>ProductCursor</c>): base64 of the sort key (placedAt, unix ms)
/// plus the order id as tiebreaker. Orders are always sorted placedAt desc, id desc.
/// </summary>
public sealed record OrderCursor(long PlacedAtMs, Guid Id)
{
    private sealed record Payload(long P, Guid I);

    public static OrderCursor For(OrderDto order) => new(order.PlacedAt.ToUnixTimeMilliseconds(), order.Id);

    public string Encode() => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Payload(PlacedAtMs, Id)));

    public static bool TryDecode(string? value, out OrderCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(Convert.FromBase64String(value)));
            if (payload is null)
            {
                return false;
            }

            cursor = new OrderCursor(payload.P, payload.I);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }
}
