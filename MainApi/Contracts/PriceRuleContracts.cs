using System.ComponentModel.DataAnnotations;

namespace MainApi.Contracts;

public sealed class QueryPriceRulesRequest : PagedQueryRequest
{
    public string Keyword { get; set; } = string.Empty;

    public bool? IsActive { get; set; }

    public string SortBy { get; set; } = "updatedAtUtc";

    public string SortDirection { get; set; } = "desc";
}

public sealed class PriceRuleResponse
{
    public long Id { get; set; }

    public string RuleType { get; set; } = string.Empty;

    public string PriceName { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public List<string> ModelTokens { get; set; } = new();

    public int RequiredQuantity { get; set; }

    public int PriceValue { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class CreatePriceRuleRequest
{
    public string RuleType { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public List<string> ModelTokens { get; set; } = new();

    [Range(0, int.MaxValue)]
    public int RequiredQuantity { get; set; }

    [Range(0, int.MaxValue)]
    public int PriceValue { get; set; }
}

public sealed class UpdatePriceRuleRequest
{
    public string RuleType { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public List<string> ModelTokens { get; set; } = new();

    [Range(0, int.MaxValue)]
    public int RequiredQuantity { get; set; }

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
    public string RuleType { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public List<string> ModelTokens { get; set; } = new();

    [Range(0, int.MaxValue)]
    public int RequiredQuantity { get; set; }

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

    public int SkippedCount { get; set; }

    public DateTime ImportedAtUtc { get; set; }
}

public sealed class PriceRuleCatalogOptionResponse
{
    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public string PriceName { get; set; } = string.Empty;

    public int ProductCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
