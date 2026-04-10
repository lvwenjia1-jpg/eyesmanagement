namespace MainApi.Domain;

public sealed class MachineCodeRecord
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
