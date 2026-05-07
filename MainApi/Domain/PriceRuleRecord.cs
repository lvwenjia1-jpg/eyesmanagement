namespace MainApi.Domain;

public static class PriceRuleTypes
{
    public const string Base = "base";
    public const string Bulk = "bulk";
    public const string Clearance = "clearance";
}

public sealed class PriceRuleRecord
{
    public long Id { get; set; }

    public string RuleType { get; set; } = PriceRuleTypes.Base;

    public string PriceName { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public int RequiredQuantity { get; set; }

    public int PriceValue { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class PriceRuleUpsertItem
{
    public string RuleType { get; set; } = PriceRuleTypes.Base;

    public string PriceName { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public int RequiredQuantity { get; set; }

    public int PriceValue { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class PriceRuleUpsertResult
{
    public int TotalCount { get; set; }

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }
}
