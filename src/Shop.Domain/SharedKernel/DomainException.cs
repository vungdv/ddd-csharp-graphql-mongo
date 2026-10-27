namespace Shop.Domain.SharedKernel;

/// <summary>Base type for expected business-rule violations; carries a stable <see cref="Code"/>.</summary>
public class DomainException(string code, string message, string? field = null) : Exception(message)
{
    public string Code { get; } = code;

    /// <summary>Offending input field for validation failures, when known.</summary>
    public string? Field { get; } = field;

    public static DomainException Validation(string field, string message) =>
        new(DomainErrorCodes.ValidationFailed, message, field);
}
