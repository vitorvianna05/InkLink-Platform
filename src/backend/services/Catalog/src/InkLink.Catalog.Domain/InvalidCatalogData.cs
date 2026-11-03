namespace InkLink.Catalog.Domain;

public sealed class InvalidCatalogData(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

internal static class CatalogGuard
{
    public static string Text(string? value, int maximum, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum)
            throw new InvalidCatalogData(field, "Value is required and must respect its maximum length.");
        return value.Trim();
    }

    public static decimal Price(decimal value, string field)
    {
        if (value < 0 || value > 9999999.99m || decimal.Round(value, 2) != value)
            throw new InvalidCatalogData(field, "Price must be non-negative, at most 9999999.99, with two decimal places.");
        return value;
    }
}
