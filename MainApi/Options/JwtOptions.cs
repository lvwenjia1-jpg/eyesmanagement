namespace MainApi.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "MainApi";

    public string Audience { get; set; } = "MainApi.Clients";

    public string SigningKey { get; set; } = "MainApi_Local_Development_Signing_Key_Change_Before_Production";

    public int ExpiresMinutes { get; set; } = 480;
}
