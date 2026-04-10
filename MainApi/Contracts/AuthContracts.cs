using System.ComponentModel.DataAnnotations;

namespace MainApi.Contracts;

public sealed class LoginRequest
{
    [Required]
    public string LoginName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string MachineCode { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public UserResponse User { get; set; } = new();
}
