namespace OrderTextTrainer.Core.Models;

public sealed class ProductAliasRule
{
    public string CanonicalName { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = new();
}
