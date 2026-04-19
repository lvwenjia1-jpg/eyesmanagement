using System.ComponentModel.DataAnnotations;

namespace MainApi.Contracts;

public sealed class QueryPriceRulesRequest : PagedQueryRequest
{
    public string Keyword { get; set; } = string.Empty;

    public bool? IsActive { get; set; }
}

public sealed class PriceRuleResponse
{
    public long Id { get; set; }

    public string PriceName { get; set; } = string.Empty;

    public int PriceValue { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class CreatePriceRuleRequest
{
    [Required]
    public string PriceName { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int PriceValue { get; set; }
}

public sealed class UpdatePriceRuleRequest
{
    [Required]
    public string PriceName { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int PriceValue { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class ImportPriceRulesRequest
{
    public string SourceFileName { get; set; } = string.Empty;

    [MinLength(1)]
    public List<ImportPriceRuleItemRequest> Entries { get; set; } = new();
}

public sealed class ImportPriceRuleItemRequest
{
    [Required]
    public string PriceName { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int PriceValue { get; set; }

    public bool? IsActive { get; set; }
}

public sealed class ImportPriceRulesResponse
{
    public string SourceFileName { get; set; } = string.Empty;

    public int TotalCount { get; set; }

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }

    public DateTime ImportedAtUtc { get; set; }
}
