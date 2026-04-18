using System.ComponentModel.DataAnnotations;

namespace MainApi.Contracts;

public sealed class QueryUsersRequest : PagedQueryRequest
{
    public string Keyword { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool? IsActive { get; set; }
}

public sealed class CreateUserRequest
{
    [Required]
    public string LoginName { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string ErpId { get; set; } = string.Empty;
}

public sealed class UpdateUserRequest
{
    [Required]
    public string LoginName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    [Required]
    public string ErpId { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public sealed class UserResponse
{
    public long Id { get; set; }

    public string LoginName { get; set; } = string.Empty;

    public string ErpId { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
