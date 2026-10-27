namespace Shop.Domain.SharedKernel;

/// <summary>Stable error codes (spec 4.5).</summary>
public static class DomainErrorCodes
{
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";
    public const string ProductSkuAlreadyExists = "PRODUCT_SKU_ALREADY_EXISTS";
    public const string ProductInactive = "PRODUCT_INACTIVE";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string InvalidMoney = "INVALID_MONEY";
    public const string OrderNotFound = "ORDER_NOT_FOUND";
    public const string OrderAlreadyCancelled = "ORDER_ALREADY_CANCELLED";
    public const string OrderEmpty = "ORDER_EMPTY";
    public const string OrderDuplicateProduct = "ORDER_DUPLICATE_PRODUCT";
    public const string CurrencyMismatch = "CURRENCY_MISMATCH";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string ValidationFailed = "VALIDATION_FAILED";
}
