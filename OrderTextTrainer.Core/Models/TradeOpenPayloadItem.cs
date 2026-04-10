using System.Text.Json.Serialization;

namespace OrderTextTrainer.Core.Models;

public sealed class TradeOpenPayloadItem
{
    [JsonPropertyName("goods_code")]
    public string GoodsCode { get; set; } = string.Empty;

    [JsonPropertyName("goods_name")]
    public string GoodsName { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("power")]
    public string? Power { get; set; }

    [JsonPropertyName("is_trial")]
    public bool IsTrial { get; set; }
}
