namespace MainApi.Domain;

public static class UserRoles
{
    public const string User = "user";
    public const string Manager = "manager";

    public static string Normalize(string? role)
    {
        var normalized = (role ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            User => User,
            Manager => Manager,
            "admin" => Manager,
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
        return normalized == Manager || normalized == User;
    }

    public static bool RequiresErpId(string? role)
    {
        return IsValid(role);
    }
}
