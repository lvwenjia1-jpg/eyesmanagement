namespace MainApi.Domain;

public static class UserRoles
{
    public const string User = "user";
    public const string Manager = "manager";
    public const string Admin = "admin";

    public static string Normalize(string? role)
    {
        var normalized = (role ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            User => User,
            Manager => Manager,
            Admin => Admin,
            _ => string.Empty
        };
    }

    public static bool IsValid(string? role)
    {
        return !string.IsNullOrWhiteSpace(Normalize(role));
    }

    public static bool CanAccessDashboard(string? role)
    {
        var normalized = Normalize(role);
        return normalized == Manager || normalized == Admin;
    }

    public static bool RequiresErpId(string? role)
    {
        return Normalize(role) == User;
    }
}
