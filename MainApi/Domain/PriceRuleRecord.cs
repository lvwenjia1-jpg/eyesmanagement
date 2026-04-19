namespace MainApi.Domain;

public sealed class PriceRuleRecord
{
    public long Id { get; set; }

    public string PriceName { get; set; } = string.Empty;

    public int PriceValue { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class PriceRuleUpsertItem
{
    public string PriceName { get; set; } = string.Empty;

    public int PriceValue { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class PriceRuleUpsertResult
{
    public int TotalCount { get; set; }

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }
}
