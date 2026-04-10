using System.Text.Json.Serialization;

namespace OrderTextTrainer.Core.Models;

public sealed class TradeOpenPayload
{
    [JsonPropertyName("trade_id")]
    public string TradeId { get; set; } = string.Empty;

    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("buyer_nick")]
    public string BuyerNick { get; set; } = string.Empty;

    [JsonPropertyName("receiver_name")]
    public string ReceiverName { get; set; } = string.Empty;

    [JsonPropertyName("receiver_mobile")]
    public string ReceiverMobile { get; set; } = string.Empty;

    [JsonPropertyName("receiver_state")]
    public string ReceiverState { get; set; } = string.Empty;

    [JsonPropertyName("receiver_city")]
    public string ReceiverCity { get; set; } = string.Empty;

    [JsonPropertyName("receiver_district")]
    public string ReceiverDistrict { get; set; } = string.Empty;

    [JsonPropertyName("receiver_address")]
    public string ReceiverAddress { get; set; } = string.Empty;

    [JsonPropertyName("seller_memo")]
    public string? SellerMemo { get; set; }

    [JsonPropertyName("trade_details")]
    public List<TradeOpenPayloadItem> TradeDetails { get; set; } = new();
}
