namespace MainApi.Domain;

public sealed class PagedQueryResult<TItem>
{
    public int TotalCount { get; set; }

    public int PageNumber { get; set; }

    public int PageSize { get; set; }

    public IReadOnlyList<TItem> Items { get; set; } = Array.Empty<TItem>();
}

public sealed class UserQuery
{
    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 50;

    public string Keyword { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool? IsActive { get; set; }
}

public sealed class MachineQuery
{
    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 50;

    public string Keyword { get; set; } = string.Empty;

    public bool? IsActive { get; set; }
}

public sealed class ProductCatalogQuery
{
    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 50;

    public string Keyword { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string Degree { get; set; } = string.Empty;
}
