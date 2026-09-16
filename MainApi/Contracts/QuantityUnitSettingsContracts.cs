namespace MainApi.Contracts;

public sealed class QuantityUnitSettingsResponse
{
    public List<QuantityUnitSettingItem> Items { get; set; } = new();
}

public sealed class QuantityUnitSettingItem
{
    public string Unit { get; set; } = string.Empty;

    public int ActualQuantity { get; set; }

    public int SortOrder { get; set; }
}

public sealed class UpdateQuantityUnitSettingsRequest
{
    public List<QuantityUnitSettingItem> Items { get; set; } = new();
}
