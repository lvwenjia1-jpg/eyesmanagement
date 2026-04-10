namespace OrderTextTrainer.Core.Models;

public sealed class TrainingSample
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public string RawText { get; set; } = string.Empty;

    public string? Notes { get; set; }
}
