namespace MainApi.Options;

public sealed class HupunOpenApiOptions
{
    public const string SectionName = "HupunOpenApi";

    public string ApiUrl { get; set; } = "https://open-api.hupun.com/api/erp/b2c/trades/open";

    public string AppKey { get; set; } = "3265462141";

    public string Secret { get; set; } = "f6e4545651378a179add862e6654327c";
}
