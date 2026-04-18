namespace MainApi.Domain;

public sealed class UserRecord
{
    public long Id { get; set; }

    public string LoginName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string PasswordSalt { get; set; } = string.Empty;

    public string ErpId { get; set; } = string.Empty;

    public string Role { get; set; } = "user";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
