using System.ComponentModel.DataAnnotations;

namespace MainApi.Contracts;

public sealed class CreateMachineRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;
}

public sealed class UpdateMachineRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public sealed class MachineResponse
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
