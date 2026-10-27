using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Shop.Application.Catalog;

/// <summary>
/// Opaque pagination cursor: base64 of the sort key plus the product id (tiebreaker). The sort field and
/// direction are embedded so a cursor cannot be replayed against a different ordering.
/// </summary>
public sealed record ProductCursor(ProductSortField Field, SortDirection Direction, string Key, Guid Id)
{
    private sealed record Payload(int F, int D, string K, Guid I);

    public static ProductCursor For(ProductSort sort, ProductDto item) =>
        new(sort.Field, sort.Direction, KeyOf(sort.Field, item), item.Id);

    public static string KeyOf(ProductSortField field, ProductDto item) => field switch
    {
        ProductSortField.Name => item.Name,
        ProductSortField.Price => item.Price.Amount.ToString(CultureInfo.InvariantCulture),
        ProductSortField.CreatedAt => item.CreatedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    public string Encode() =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Payload((int)Field, (int)Direction, Key, Id)));

    public static bool TryDecode(string? value, out ProductCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(Convert.FromBase64String(value)));
            if (payload is null
                || !Enum.IsDefined((ProductSortField)payload.F)
                || !Enum.IsDefined((SortDirection)payload.D)
                || payload.K is null
                || !KeyIsValid((ProductSortField)payload.F, payload.K))
            {
                return false;
            }

            cursor = new ProductCursor((ProductSortField)payload.F, (SortDirection)payload.D, payload.K, payload.I);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static bool KeyIsValid(ProductSortField field, string key) => field switch
    {
        ProductSortField.Name => true,
        ProductSortField.Price => decimal.TryParse(key, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
        _ => long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
    };
}
