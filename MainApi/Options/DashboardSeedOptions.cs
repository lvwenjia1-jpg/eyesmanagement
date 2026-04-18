namespace MainApi.Options;

public sealed class DashboardSeedOptions
{
    public const string SectionName = "DashboardSeed";

    public bool Enabled { get; set; } = true;

    public bool ResetExistingData { get; set; }

    public int BusinessGroupCount { get; set; } = 6;

    public int OrdersPerGroup { get; set; } = 12;
}
