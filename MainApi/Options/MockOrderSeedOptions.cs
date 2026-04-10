namespace MainApi.Options;

public sealed class MockOrderSeedOptions
{
    public const string SectionName = "MockOrderSeed";

    public bool Enabled { get; set; }

    public bool ResetExistingUploads { get; set; }

    public bool SkipWhenUploadsExist { get; set; } = true;

    public int Days { get; set; } = 30;

    public int OrdersPerDay { get; set; } = 500;

    public int MaxItemsPerOrder { get; set; } = 4;
}
