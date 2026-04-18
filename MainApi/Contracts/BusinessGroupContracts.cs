using System.ComponentModel.DataAnnotations;

namespace MainApi.Contracts;

public sealed class QueryBusinessGroupsRequest : PagedQueryRequest
{
    public string Keyword { get; set; } = string.Empty;
}

public sealed class BusinessGroupResponse
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    public int OrderCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class UpdateBusinessGroupBalanceRequest
{
    [Range(0, double.MaxValue)]
    public decimal Balance { get; set; }
}
