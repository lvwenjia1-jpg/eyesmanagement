namespace MainApi.Contracts;

public sealed class WearPeriodSettingsResponse
{
    public List<WearPeriodItemResponse> WearPeriods { get; set; } = new();

    public List<WearPeriodAliasItemResponse> WearPeriodMappings { get; set; } = new();
}

public sealed class WearPeriodItemResponse
{
    public string Value { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

public sealed class WearPeriodAliasItemResponse
{
    public string Alias { get; set; } = string.Empty;

    public string WearPeriod { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

public sealed class UpdateWearPeriodSettingsRequest
{
    public List<WearPeriodItemRequest> WearPeriods { get; set; } = new();

    public List<WearPeriodAliasItemRequest> WearPeriodMappings { get; set; } = new();
}

public sealed class WearPeriodItemRequest
{
    public string Value { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

public sealed class WearPeriodAliasItemRequest
{
    public string Alias { get; set; } = string.Empty;

    public string WearPeriod { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
