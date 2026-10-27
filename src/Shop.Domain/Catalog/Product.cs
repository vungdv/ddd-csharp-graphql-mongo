using Shop.Domain.SharedKernel;

namespace Shop.Domain.Catalog;

public sealed class Product : AggregateRoot<ProductId>
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxCategoryLength = 100;

    // Used by persistence rehydration only.
    private Product()
    {
        Sku = null!;
        Name = null!;
        Price = null!;
        Category = null!;
    }

    public Sku Sku { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public Money Price { get; private set; }

    public string Category { get; private set; }

    public int StockQuantity { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Product Create(
        Sku sku,
        string name,
        string? description,
        Money price,
        string category,
        int initialStock,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(clock);
        EnsurePositive(price);
        if (initialStock < 0)
        {
            throw DomainException.Validation("initialStock", "Initial stock must not be negative.");
        }

        var now = clock.UtcNow;
        var product = new Product
        {
            Id = ProductId.New(),
            Sku = sku,
            Name = ValidateName(name),
            Description = ValidateDescription(description),
            Price = price,
            Category = ValidateCategory(category),
            StockQuantity = initialStock,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        product.Raise(new ProductCreated(product.Id, sku, now));
        return product;
    }

    public void Rename(string name, IClock clock)
    {
        Name = ValidateName(name);
        Touch(clock);
    }

    public void ChangeDescription(string? description, IClock clock)
    {
        Description = ValidateDescription(description);
        Touch(clock);
    }

    public void ChangeCategory(string category, IClock clock)
    {
        Category = ValidateCategory(category);
        Touch(clock);
    }

    public void ChangePrice(Money newPrice, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newPrice);
        EnsurePositive(newPrice);
        if (newPrice == Price)
        {
            return;
        }

        var old = Price;
        Price = newPrice;
        Touch(clock);
        Raise(new ProductPriceChanged(Id, old, newPrice, UpdatedAt));
    }

    public void Activate(IClock clock)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Touch(clock);
    }

    public void Deactivate(IClock clock)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Touch(clock);
    }

    public void AdjustStock(int delta, IClock clock)
    {
        var result = (long)StockQuantity + delta;
        if (result < 0)
        {
            throw InsufficientStock(-delta);
        }

        if (result > int.MaxValue)
        {
            throw DomainException.Validation("delta", "Stock quantity overflow.");
        }

        StockQuantity = (int)result;
        Touch(clock);
    }

    public void DecreaseStock(int quantity, IClock clock)
    {
        if (quantity <= 0)
        {
            throw DomainException.Validation("quantity", "Quantity must be greater than zero.");
        }

        if (quantity > StockQuantity)
        {
            throw InsufficientStock(quantity);
        }

        StockQuantity -= quantity;
        Touch(clock);
    }

    private DomainException InsufficientStock(int requested) =>
        new(
            DomainErrorCodes.InsufficientStock,
            $"Insufficient stock for {Sku}: requested {requested}, available {StockQuantity}.");

    private void Touch(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        UpdatedAt = clock.UtcNow;
    }

    private static void EnsurePositive(Money price)
    {
        if (price.Amount <= 0)
        {
            throw new DomainException(DomainErrorCodes.InvalidMoney, "Price must be greater than zero.");
        }
    }

    private static string ValidateName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength
            ? throw DomainException.Validation("name", $"Name is required and at most {MaxNameLength} characters.")
            : name;

    private static string? ValidateDescription(string? description) =>
        description is { Length: > MaxDescriptionLength }
            ? throw DomainException.Validation("description", $"Description must be at most {MaxDescriptionLength} characters.")
            : description;

    private static string ValidateCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) || category.Length > MaxCategoryLength
            ? throw DomainException.Validation("category", $"Category is required and at most {MaxCategoryLength} characters.")
            : category;
}
