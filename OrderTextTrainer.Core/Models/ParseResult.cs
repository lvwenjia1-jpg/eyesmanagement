namespace OrderTextTrainer.Core.Models;

public sealed class ParseResult
{
    public string NormalizedText { get; set; } = string.Empty;

    public List<ParsedOrder> Orders { get; set; } = new();

    public List<string> UnknownSegments { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
}
