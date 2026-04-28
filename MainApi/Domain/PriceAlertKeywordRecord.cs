namespace MainApi.Domain;

public sealed class PriceAlertKeywordRecord
{
    public long Id { get; set; }

    public string Keyword { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
