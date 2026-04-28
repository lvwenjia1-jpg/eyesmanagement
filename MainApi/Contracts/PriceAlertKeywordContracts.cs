using System.ComponentModel.DataAnnotations;

namespace MainApi.Contracts;

public sealed class PriceAlertKeywordResponse
{
    public long Id { get; set; }

    public string Keyword { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class CreatePriceAlertKeywordRequest
{
    [Required]
    public string Keyword { get; set; } = string.Empty;
}

public sealed class UpdatePriceAlertKeywordRequest
{
    [Required]
    public string Keyword { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
