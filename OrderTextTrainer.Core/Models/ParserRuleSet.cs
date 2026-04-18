namespace OrderTextTrainer.Core.Models;

public sealed class ParserRuleSet
{
    public Dictionary<string, List<string>> BrandAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> WearTypeAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ProductAliasRule> ProductAliases { get; set; } = new();

    public List<string> NoiseKeywords { get; set; } = new();

    public List<string> GiftKeywords { get; set; } = new();

    public List<string> AddressKeywords { get; set; } = new();

    public List<string> NameLabels { get; set; } = new();

    public List<string> PhoneLabels { get; set; } = new();

    public List<string> AddressLabels { get; set; } = new();

    public static ParserRuleSet CreateDefault()
    {
        return new ParserRuleSet
        {
            BrandAliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Lenspop"] = new() { "lenspop", "LENSPOP" },
                ["LEEA"] = new() { "leea", "LEEA", "莉亚", "LEEA莉亚" }
            },
            WearTypeAliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["日抛10片装"] = new() { "日抛10片装", "日抛十片装", "日抛10片", "日抛十片", "日拋10片裝", "日拋十片裝" },
                ["日抛2片装"] = new() { "日抛2片装", "日抛两片装", "日抛2片", "日抛两片", "新日抛2片", "日拋2片裝", "日拋兩片裝" },
                ["日抛"] = new() { "日抛", "日拋" },
                ["半年抛"] = new() { "半年抛", "半年拋" },
                ["年抛"] = new() { "年抛", "年拋" },
                ["试戴片"] = new() { "试戴片", "試戴片", "试用" }
            },
            ProductAliases = new List<ProductAliasRule>
            {
                new() { CanonicalName = "溏心珠蓝绿", Aliases = new() { "溏心珠蓝绿", "糖心珠蓝绿" } },
                new() { CanonicalName = "溏心珠绿", Aliases = new() { "溏心珠绿", "糖心珠绿" } },
                new() { CanonicalName = "溏心珠灰粉", Aliases = new() { "溏心珠灰粉", "糖心珠灰粉" } },
                new() { CanonicalName = "溏心珠灰", Aliases = new() { "溏心珠灰", "糖心珠灰" } },
                new() { CanonicalName = "奶冻冰球红", Aliases = new() { "奶冻冰球红" } },
                new() { CanonicalName = "奶冻冰球蓝", Aliases = new() { "奶冻冰球蓝" } },
                new() { CanonicalName = "奶冻冰球青", Aliases = new() { "奶冻冰球青" } },
                new() { CanonicalName = "光织蓝", Aliases = new() { "光织蓝" } },
                new() { CanonicalName = "光织绿", Aliases = new() { "光织绿" } },
                new() { CanonicalName = "初雪音律", Aliases = new() { "初雪音律" } },
                new() { CanonicalName = "冰沙糖紫", Aliases = new() { "冰沙糖紫" } },
                new() { CanonicalName = "次元梦境Pro粉", Aliases = new() { "次元梦境pro粉", "次元梦境Pro粉", "次元梦境粉" } },
                new() { CanonicalName = "次元梦境Pro紫", Aliases = new() { "次元梦境pro紫", "次元梦境Pro紫", "次元梦境紫" } },
                new() { CanonicalName = "次元梦境蓝", Aliases = new() { "次元梦境蓝", "次元梦镜蓝" } },
                new() { CanonicalName = "次元梦境棕", Aliases = new() { "次元梦境棕", "次元梦境茶棕" } },
                new() { CanonicalName = "笼中梦棕", Aliases = new() { "笼中梦棕" } },
                new() { CanonicalName = "月光茶盏粉", Aliases = new() { "月光茶盏粉", "月光茶盞粉" } },
                new() { CanonicalName = "月光茶盏棕", Aliases = new() { "月光茶盏棕", "月光茶盞棕" } },
                new() { CanonicalName = "云隙微光粉", Aliases = new() { "云隙微光粉", "雲隙微光粉" } },
                new() { CanonicalName = "云隙微光灰", Aliases = new() { "云隙微光灰", "雲隙微光灰" } },
                new() { CanonicalName = "云隙微光棕", Aliases = new() { "云隙微光棕", "雲隙微光棕" } },
                new() { CanonicalName = "冰透风铃紫", Aliases = new() { "冰透风铃紫", "冰透風鈴紫" } },
                new() { CanonicalName = "星辰泪橘棕", Aliases = new() { "星辰泪橘棕", "LENSPOP星辰泪橘棕", "lenspop星辰泪橘棕" } },
                new() { CanonicalName = "星辰泪紫", Aliases = new() { "星辰泪紫", "新日抛2片星辰泪紫", "新包装日抛2片星辰泪紫" } },
                new() { CanonicalName = "星辰泪金棕", Aliases = new() { "星辰泪金棕" } },
                new() { CanonicalName = "星辰泪青", Aliases = new() { "星辰泪青" } },
                new() { CanonicalName = "星辰泪蓝", Aliases = new() { "星辰泪蓝" } },
                new() { CanonicalName = "流萤森金棕", Aliases = new() { "流萤森金棕" } },
                new() { CanonicalName = "绘世纱蓝", Aliases = new() { "绘世纱蓝", "繪世紗藍" } },
                new() { CanonicalName = "绘世纱紫", Aliases = new() { "绘世纱紫", "繪世紗紫" } }
            },
            NoiseKeywords = new List<string>
            {
                "活动", "备注", "下单", "品牌", "款式", "订单", "收件人", "联系方式"
            },
            GiftKeywords = new List<string>
            {
                "赠品", "赠送", "护理液", "护理盒", "伴侣盒", "盒子"
            },
            AddressKeywords = new List<string>
            {
                "省", "市", "区", "县", "镇", "乡", "街道", "大道", "路", "号", "楼", "单元", "室", "园", "村", "仓", "驿站", "校区", "大厦", "家园"
            },
            NameLabels = new List<string> { "姓名", "名字", "收件人", "收货人", "客户" },
            PhoneLabels = new List<string> { "电话", "手机", "联系方式", "联系号码" },
            AddressLabels = new List<string> { "地址", "收货地址", "配送地址", "所在地区" }
        };
    }
}

