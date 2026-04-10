using System.ComponentModel.DataAnnotations;

namespace MainApi.Contracts;

public sealed class CreateUserRequest
{
    [Required]
    public string LoginName { get; set; } = string.Empty;

    [Required]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string ErpId { get; set; } = string.Empty;

    [Required]
    public string WecomId { get; set; } = string.Empty;

    public string Role { get; set; } = "user";
}

public sealed class UpdateUserRequest
{
    [Required]
    public string DisplayName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    [Required]
    public string ErpId { get; set; } = string.Empty;

    [Required]
    public string WecomId { get; set; } = string.Empty;

    public string Role { get; set; } = "user";

    public bool IsActive { get; set; } = true;
}

public sealed class UserResponse
{
    public long Id { get; set; }

    public string LoginName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string ErpId { get; set; } = string.Empty;

    public string WecomId { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
