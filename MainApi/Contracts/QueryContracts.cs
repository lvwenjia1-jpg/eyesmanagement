using System.ComponentModel.DataAnnotations;

namespace MainApi.Contracts;

public sealed class PagedResponse<TItem>
{
    public int TotalCount { get; set; }

    public int PageNumber { get; set; }

    public int PageSize { get; set; }

    public IReadOnlyList<TItem> Items { get; set; } = Array.Empty<TItem>();
}

public abstract class PagedQueryRequest
{
    [Range(1, int.MaxValue)]
    public int PageNumber { get; set; } = 1;

    [Range(1, 500)]
    public int PageSize { get; set; } = 50;
}
