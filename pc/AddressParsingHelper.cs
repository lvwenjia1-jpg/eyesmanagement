using System.Text.RegularExpressions;

namespace WpfApp11;

internal static class AddressParsingHelper
{
    private static readonly Regex PlaceholderPrefixRegex = new(
        @"^(?:(?:null|undefined)\s*)+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PlaceholderOnlyRegex = new(
        @"^(?:null|undefined)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UploadAddressLabelRegex = new(
        @"^(?:(?:收货地址|收件地址|详细地址|地址|所在地区|地区)\s*[:：]?\s*)+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex GenericProvinceRegex = new(
        @"^(?<province>.+?(?:省|自治区|特别行政区|市))",
        RegexOptions.Compiled);

    private static readonly Regex GenericCityRegex = new(
        @"^(?<city>.+?(?:自治州|地区|盟|市))",
        RegexOptions.Compiled);

    private static readonly Regex GenericAreaRegex = new(
        @"^(?<area>.+?(?:自治县|林区|特区|新区|矿区|区|县|旗|市))",
        RegexOptions.Compiled);

    private static readonly string[] ProvinceAliasSuffixes =
    {
        "维吾尔自治区",
        "壮族自治区",
        "回族自治区",
        "特别行政区",
        "自治区",
        "省",
        "市"
    };

    private static readonly string[] CityAliasSuffixes =
    {
        "自治州",
        "地区",
        "盟",
        "市"
    };

    private static readonly string[] ProvinceLevelRegions =
    {
        "新疆维吾尔自治区",
        "内蒙古自治区",
        "广西壮族自治区",
        "宁夏回族自治区",
        "西藏自治区",
        "香港特别行政区",
        "澳门特别行政区",
        "新疆生产建设兵团",
        "黑龙江省",
        "吉林省",
        "辽宁省",
        "河北省",
        "山西省",
        "陕西省",
        "甘肃省",
        "青海省",
        "山东省",
        "江苏省",
        "浙江省",
        "安徽省",
        "福建省",
        "江西省",
        "河南省",
        "湖北省",
        "湖南省",
        "广东省",
        "海南省",
        "四川省",
        "贵州省",
        "云南省",
        "台湾省",
        "北京市",
        "上海市",
        "天津市",
        "重庆市"
    };

    private static readonly CityRegionEntry[] CityLevelRegions =
    {
        new("黑龙江省", "哈尔滨市"),
        new("黑龙江省", "齐齐哈尔市"),
        new("黑龙江省", "鸡西市"),
        new("黑龙江省", "鹤岗市"),
        new("黑龙江省", "双鸭山市"),
        new("黑龙江省", "大庆市"),
        new("黑龙江省", "伊春市"),
        new("黑龙江省", "佳木斯市"),
        new("黑龙江省", "七台河市"),
        new("黑龙江省", "牡丹江市"),
        new("黑龙江省", "黑河市"),
        new("黑龙江省", "绥化市"),
        new("黑龙江省", "大兴安岭地区"),
        new("吉林省", "长春市"),
        new("吉林省", "吉林市"),
        new("吉林省", "四平市"),
        new("吉林省", "辽源市"),
        new("吉林省", "通化市"),
        new("吉林省", "白山市"),
        new("吉林省", "松原市"),
        new("吉林省", "白城市"),
        new("吉林省", "延边朝鲜族自治州"),
        new("辽宁省", "沈阳市"),
        new("辽宁省", "大连市"),
        new("辽宁省", "鞍山市"),
        new("辽宁省", "抚顺市"),
        new("辽宁省", "本溪市"),
        new("辽宁省", "丹东市"),
        new("辽宁省", "锦州市"),
        new("辽宁省", "营口市"),
        new("辽宁省", "阜新市"),
        new("辽宁省", "辽阳市"),
        new("辽宁省", "盘锦市"),
        new("辽宁省", "铁岭市"),
        new("辽宁省", "朝阳市"),
        new("辽宁省", "葫芦岛市"),
        new("河北省", "石家庄市"),
        new("河北省", "唐山市"),
        new("河北省", "秦皇岛市"),
        new("河北省", "邯郸市"),
        new("河北省", "邢台市"),
        new("河北省", "保定市"),
        new("河北省", "张家口市"),
        new("河北省", "承德市"),
        new("河北省", "沧州市"),
        new("河北省", "廊坊市"),
        new("河北省", "衡水市"),
        new("山西省", "太原市"),
        new("山西省", "大同市"),
        new("山西省", "阳泉市"),
        new("山西省", "长治市"),
        new("山西省", "晋城市"),
        new("山西省", "朔州市"),
        new("山西省", "晋中市"),
        new("山西省", "运城市"),
        new("山西省", "忻州市"),
        new("山西省", "临汾市"),
        new("山西省", "吕梁市"),
        new("陕西省", "西安市"),
        new("陕西省", "铜川市"),
        new("陕西省", "宝鸡市"),
        new("陕西省", "咸阳市"),
        new("陕西省", "渭南市"),
        new("陕西省", "延安市"),
        new("陕西省", "汉中市"),
        new("陕西省", "榆林市"),
        new("陕西省", "安康市"),
        new("陕西省", "商洛市"),
        new("甘肃省", "兰州市"),
        new("甘肃省", "嘉峪关市"),
        new("甘肃省", "金昌市"),
        new("甘肃省", "白银市"),
        new("甘肃省", "天水市"),
        new("甘肃省", "武威市"),
        new("甘肃省", "张掖市"),
        new("甘肃省", "平凉市"),
        new("甘肃省", "酒泉市"),
        new("甘肃省", "庆阳市"),
        new("甘肃省", "定西市"),
        new("甘肃省", "陇南市"),
        new("甘肃省", "临夏回族自治州"),
        new("甘肃省", "甘南藏族自治州"),
        new("青海省", "西宁市"),
        new("青海省", "海东市"),
        new("青海省", "海北藏族自治州"),
        new("青海省", "黄南藏族自治州"),
        new("青海省", "海南藏族自治州"),
        new("青海省", "果洛藏族自治州"),
        new("青海省", "玉树藏族自治州"),
        new("青海省", "海西蒙古族藏族自治州"),
        new("山东省", "济南市"),
        new("山东省", "青岛市"),
        new("山东省", "淄博市"),
        new("山东省", "枣庄市"),
        new("山东省", "东营市"),
        new("山东省", "烟台市"),
        new("山东省", "潍坊市"),
        new("山东省", "济宁市"),
        new("山东省", "泰安市"),
        new("山东省", "威海市"),
        new("山东省", "日照市"),
        new("山东省", "临沂市"),
        new("山东省", "德州市"),
        new("山东省", "聊城市"),
        new("山东省", "滨州市"),
        new("山东省", "菏泽市"),
        new("江苏省", "南京市"),
        new("江苏省", "无锡市"),
        new("江苏省", "徐州市"),
        new("江苏省", "常州市"),
        new("江苏省", "苏州市"),
        new("江苏省", "南通市"),
        new("江苏省", "连云港市"),
        new("江苏省", "淮安市"),
        new("江苏省", "盐城市"),
        new("江苏省", "扬州市"),
        new("江苏省", "镇江市"),
        new("江苏省", "泰州市"),
        new("江苏省", "宿迁市"),
        new("浙江省", "杭州市"),
        new("浙江省", "宁波市"),
        new("浙江省", "温州市"),
        new("浙江省", "嘉兴市"),
        new("浙江省", "湖州市"),
        new("浙江省", "绍兴市"),
        new("浙江省", "金华市"),
        new("浙江省", "衢州市"),
        new("浙江省", "舟山市"),
        new("浙江省", "台州市"),
        new("浙江省", "丽水市"),
        new("安徽省", "合肥市"),
        new("安徽省", "芜湖市"),
        new("安徽省", "蚌埠市"),
        new("安徽省", "淮南市"),
        new("安徽省", "马鞍山市"),
        new("安徽省", "淮北市"),
        new("安徽省", "铜陵市"),
        new("安徽省", "安庆市"),
        new("安徽省", "黄山市"),
        new("安徽省", "滁州市"),
        new("安徽省", "阜阳市"),
        new("安徽省", "宿州市"),
        new("安徽省", "六安市"),
        new("安徽省", "亳州市"),
        new("安徽省", "池州市"),
        new("安徽省", "宣城市"),
        new("福建省", "福州市"),
        new("福建省", "厦门市"),
        new("福建省", "莆田市"),
        new("福建省", "三明市"),
        new("福建省", "泉州市"),
        new("福建省", "漳州市"),
        new("福建省", "南平市"),
        new("福建省", "龙岩市"),
        new("福建省", "宁德市"),
        new("江西省", "南昌市"),
        new("江西省", "景德镇市"),
        new("江西省", "萍乡市"),
        new("江西省", "九江市"),
        new("江西省", "新余市"),
        new("江西省", "鹰潭市"),
        new("江西省", "赣州市"),
        new("江西省", "吉安市"),
        new("江西省", "宜春市"),
        new("江西省", "抚州市"),
        new("江西省", "上饶市"),
        new("河南省", "郑州市"),
        new("河南省", "开封市"),
        new("河南省", "洛阳市"),
        new("河南省", "平顶山市"),
        new("河南省", "安阳市"),
        new("河南省", "鹤壁市"),
        new("河南省", "新乡市"),
        new("河南省", "焦作市"),
        new("河南省", "濮阳市"),
        new("河南省", "许昌市"),
        new("河南省", "漯河市"),
        new("河南省", "三门峡市"),
        new("河南省", "南阳市"),
        new("河南省", "商丘市"),
        new("河南省", "信阳市"),
        new("河南省", "周口市"),
        new("河南省", "驻马店市"),
        new("河南省", "济源市"),
        new("湖北省", "武汉市"),
        new("湖北省", "黄石市"),
        new("湖北省", "十堰市"),
        new("湖北省", "宜昌市"),
        new("湖北省", "襄阳市"),
        new("湖北省", "鄂州市"),
        new("湖北省", "荆门市"),
        new("湖北省", "孝感市"),
        new("湖北省", "荆州市"),
        new("湖北省", "黄冈市"),
        new("湖北省", "咸宁市"),
        new("湖北省", "随州市"),
        new("湖北省", "恩施土家族苗族自治州"),
        new("湖南省", "长沙市"),
        new("湖南省", "株洲市"),
        new("湖南省", "湘潭市"),
        new("湖南省", "衡阳市"),
        new("湖南省", "邵阳市"),
        new("湖南省", "岳阳市"),
        new("湖南省", "常德市"),
        new("湖南省", "张家界市"),
        new("湖南省", "益阳市"),
        new("湖南省", "郴州市"),
        new("湖南省", "永州市"),
        new("湖南省", "怀化市"),
        new("湖南省", "娄底市"),
        new("湖南省", "湘西土家族苗族自治州"),
        new("广东省", "广州市"),
        new("广东省", "韶关市"),
        new("广东省", "深圳市"),
        new("广东省", "珠海市"),
        new("广东省", "汕头市"),
        new("广东省", "佛山市"),
        new("广东省", "江门市"),
        new("广东省", "湛江市"),
        new("广东省", "茂名市"),
        new("广东省", "肇庆市"),
        new("广东省", "惠州市"),
        new("广东省", "梅州市"),
        new("广东省", "汕尾市"),
        new("广东省", "河源市"),
        new("广东省", "阳江市"),
        new("广东省", "清远市"),
        new("广东省", "东莞市"),
        new("广东省", "中山市"),
        new("广东省", "潮州市"),
        new("广东省", "揭阳市"),
        new("广东省", "云浮市"),
        new("海南省", "海口市"),
        new("海南省", "三亚市"),
        new("海南省", "三沙市"),
        new("海南省", "儋州市"),
        new("四川省", "成都市"),
        new("四川省", "自贡市"),
        new("四川省", "攀枝花市"),
        new("四川省", "泸州市"),
        new("四川省", "德阳市"),
        new("四川省", "绵阳市"),
        new("四川省", "广元市"),
        new("四川省", "遂宁市"),
        new("四川省", "内江市"),
        new("四川省", "乐山市"),
        new("四川省", "南充市"),
        new("四川省", "眉山市"),
        new("四川省", "宜宾市"),
        new("四川省", "广安市"),
        new("四川省", "达州市"),
        new("四川省", "雅安市"),
        new("四川省", "巴中市"),
        new("四川省", "资阳市"),
        new("四川省", "阿坝藏族羌族自治州"),
        new("四川省", "甘孜藏族自治州"),
        new("四川省", "凉山彝族自治州"),
        new("贵州省", "贵阳市"),
        new("贵州省", "六盘水市"),
        new("贵州省", "遵义市"),
        new("贵州省", "安顺市"),
        new("贵州省", "毕节市"),
        new("贵州省", "铜仁市"),
        new("贵州省", "黔西南布依族苗族自治州"),
        new("贵州省", "黔东南苗族侗族自治州"),
        new("贵州省", "黔南布依族苗族自治州"),
        new("云南省", "昆明市"),
        new("云南省", "曲靖市"),
        new("云南省", "玉溪市"),
        new("云南省", "保山市"),
        new("云南省", "昭通市"),
        new("云南省", "丽江市"),
        new("云南省", "普洱市"),
        new("云南省", "临沧市"),
        new("云南省", "楚雄彝族自治州"),
        new("云南省", "红河哈尼族彝族自治州"),
        new("云南省", "文山壮族苗族自治州"),
        new("云南省", "西双版纳傣族自治州"),
        new("云南省", "大理白族自治州"),
        new("云南省", "德宏傣族景颇族自治州"),
        new("云南省", "怒江傈僳族自治州"),
        new("云南省", "迪庆藏族自治州"),
        new("内蒙古自治区", "呼和浩特市"),
        new("内蒙古自治区", "包头市"),
        new("内蒙古自治区", "乌海市"),
        new("内蒙古自治区", "赤峰市"),
        new("内蒙古自治区", "通辽市"),
        new("内蒙古自治区", "鄂尔多斯市"),
        new("内蒙古自治区", "呼伦贝尔市"),
        new("内蒙古自治区", "巴彦淖尔市"),
        new("内蒙古自治区", "乌兰察布市"),
        new("内蒙古自治区", "兴安盟"),
        new("内蒙古自治区", "锡林郭勒盟"),
        new("内蒙古自治区", "阿拉善盟"),
        new("广西壮族自治区", "南宁市"),
        new("广西壮族自治区", "柳州市"),
        new("广西壮族自治区", "桂林市"),
        new("广西壮族自治区", "梧州市"),
        new("广西壮族自治区", "北海市"),
        new("广西壮族自治区", "防城港市"),
        new("广西壮族自治区", "钦州市"),
        new("广西壮族自治区", "贵港市"),
        new("广西壮族自治区", "玉林市"),
        new("广西壮族自治区", "百色市"),
        new("广西壮族自治区", "贺州市"),
        new("广西壮族自治区", "河池市"),
        new("广西壮族自治区", "来宾市"),
        new("广西壮族自治区", "崇左市"),
        new("宁夏回族自治区", "银川市"),
        new("宁夏回族自治区", "石嘴山市"),
        new("宁夏回族自治区", "吴忠市"),
        new("宁夏回族自治区", "固原市"),
        new("宁夏回族自治区", "中卫市"),
        new("新疆维吾尔自治区", "乌鲁木齐市"),
        new("新疆维吾尔自治区", "克拉玛依市"),
        new("新疆维吾尔自治区", "吐鲁番市"),
        new("新疆维吾尔自治区", "哈密市"),
        new("新疆维吾尔自治区", "昌吉回族自治州"),
        new("新疆维吾尔自治区", "博尔塔拉蒙古自治州"),
        new("新疆维吾尔自治区", "巴音郭楞蒙古自治州"),
        new("新疆维吾尔自治区", "阿克苏地区"),
        new("新疆维吾尔自治区", "克孜勒苏柯尔克孜自治州"),
        new("新疆维吾尔自治区", "喀什地区"),
        new("新疆维吾尔自治区", "和田地区"),
        new("新疆维吾尔自治区", "伊犁哈萨克自治州"),
        new("新疆维吾尔自治区", "塔城地区"),
        new("新疆维吾尔自治区", "阿勒泰地区"),
        new("西藏自治区", "拉萨市"),
        new("西藏自治区", "日喀则市"),
        new("西藏自治区", "昌都市"),
        new("西藏自治区", "林芝市"),
        new("西藏自治区", "山南市"),
        new("西藏自治区", "那曲市"),
        new("西藏自治区", "阿里地区")
    };

    private static readonly ProvinceAliasEntry[] ProvinceAliases = BuildProvinceAliases();
    private static readonly CityAliasEntry[] CityAliases = BuildCityAliases();

    private static readonly string[] MunicipalityConnectorTokens =
    {
        "市辖区",
        "市辖县",
        "城区",
        "县"
    };

    private static readonly Dictionary<string, string> MunicipalityShortNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["北京"] = "北京市",
        ["上海"] = "上海市",
        ["天津"] = "天津市",
        ["重庆"] = "重庆市"
    };

    private static readonly HashSet<string> MunicipalityRegions = new(StringComparer.OrdinalIgnoreCase)
    {
        "北京市",
        "上海市",
        "天津市",
        "重庆市",
        "香港特别行政区",
        "澳门特别行政区"
    };

    public static string NormalizeAddressInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        normalized = PlaceholderPrefixRegex.Replace(normalized, string.Empty).Trim();
        return PlaceholderOnlyRegex.IsMatch(normalized) ? string.Empty : normalized;
    }

    public static AddressParts SplitAddress(string? address)
    {
        var cleaned = NormalizeAddressInput(address);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return AddressParts.Empty;
        }

        const string markerPattern =
            @"^(?<state>.*?(?:省|自治区|特别行政区|市))?(?<city>.*?(?:市|自治州|地区|盟))?(?<district>.*?(?:区|县|旗|市|镇|乡|街道|苏木))?(?<detail>.*)$";
        var markerMatch = Regex.Match(cleaned, markerPattern);
        if (markerMatch.Success)
        {
            var state = markerMatch.Groups["state"].Value.Trim();
            var city = markerMatch.Groups["city"].Value.Trim();
            var district = markerMatch.Groups["district"].Value.Trim();
            var detail = markerMatch.Groups["detail"].Value.Trim();

            if (!string.IsNullOrWhiteSpace(state) ||
                !string.IsNullOrWhiteSpace(city) ||
                !string.IsNullOrWhiteSpace(district))
            {
                var markerParts = new AddressParts(state, city, district, detail);
                var fallbackParts = SplitAddressForUpload(cleaned);
                return CompareRegionCompleteness(fallbackParts, markerParts) > 0
                    ? fallbackParts
                    : markerParts;
            }
        }

        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 4)
        {
            return new AddressParts(
                tokens[0],
                tokens[1],
                tokens[2],
                string.Join(' ', tokens, 3, tokens.Length - 3));
        }

        if (tokens.Length == 3)
        {
            return new AddressParts(tokens[0], tokens[1], tokens[2], string.Empty);
        }

        if (tokens.Length == 2)
        {
            return new AddressParts(tokens[0], tokens[1], string.Empty, string.Empty);
        }

        return new AddressParts(string.Empty, string.Empty, string.Empty, cleaned);
    }

    public static AddressParts SplitAddressForUpload(string? address)
    {
        var normalized = NormalizeUploadAddressInput(address);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return AddressParts.Empty;
        }

        var compact = BuildCompactAddress(normalized);
        if (compact.Length == 0)
        {
            return new AddressParts(string.Empty, string.Empty, string.Empty, normalized);
        }

        if (!TryParseProvince(compact.Text, out var province, out var provinceConsumed))
        {
            if (!TryParseCity(compact.Text, string.Empty, out province, out var inferredCity, out provinceConsumed))
            {
                return new AddressParts(string.Empty, string.Empty, string.Empty, normalized);
            }

            var inferredArea = string.Empty;
            var inferredConsumed = provinceConsumed;
            var inferredAreaMatch = GenericAreaRegex.Match(compact.Text[inferredConsumed..]);
            if (inferredAreaMatch.Success)
            {
                inferredArea = inferredAreaMatch.Groups["area"].Value;
                inferredConsumed += inferredArea.Length;
            }

            return new AddressParts(
                province,
                inferredCity,
                inferredArea,
                ExtractDetail(normalized, compact, inferredConsumed));
        }

        var consumed = provinceConsumed;
        var city = string.Empty;
        var area = string.Empty;

        if (MunicipalityRegions.Contains(province))
        {
            city = province;
            consumed += ConsumeMunicipalityConnector(compact.Text, consumed);
            var municipalityAreaMatch = GenericAreaRegex.Match(compact.Text[consumed..]);
            if (municipalityAreaMatch.Success)
            {
                area = municipalityAreaMatch.Groups["area"].Value;
                consumed += area.Length;
            }
        }
        else
        {
            if (TryParseCity(compact.Text[consumed..], province, out _, out city, out var cityConsumed))
            {
                consumed += cityConsumed;

                var areaMatch = GenericAreaRegex.Match(compact.Text[consumed..]);
                if (areaMatch.Success)
                {
                    area = areaMatch.Groups["area"].Value;
                    consumed += area.Length;
                }
            }
            else
            {
                var cityMatch = GenericCityRegex.Match(compact.Text[consumed..]);
                if (cityMatch.Success)
                {
                    city = cityMatch.Groups["city"].Value;
                    consumed += city.Length;

                    var areaMatch = GenericAreaRegex.Match(compact.Text[consumed..]);
                    if (areaMatch.Success)
                    {
                        area = areaMatch.Groups["area"].Value;
                        consumed += area.Length;
                    }
                }
            }
        }

        return new AddressParts(
            province,
            city,
            area,
            ExtractDetail(normalized, compact, consumed));
    }

    public static AddressParts ResolveRegionParts(string? receiverRegion, string? receiverAddress)
    {
        var regionParts = SplitAddress(receiverRegion);
        var addressParts = SplitAddress(receiverAddress);
        return CompareRegionCompleteness(addressParts, regionParts) > 0
            ? addressParts
            : regionParts;
    }

    public static string CombineRegion(string? state, string? city, string? district)
    {
        return NormalizeAddressInput($"{NormalizeAddressInput(state)}{NormalizeAddressInput(city)}{NormalizeAddressInput(district)}");
    }

    private static string NormalizeUploadAddressInput(string? value)
    {
        var normalized = NormalizeAddressInput(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = UploadAddressLabelRegex.Replace(normalized, string.Empty).Trim();
        normalized = normalized.TrimStart('：', ':', '，', ',', '；', ';', '/', '\\', '-', '_');
        normalized = CollapseMunicipalityNoise(normalized);
        return normalized.Trim();
    }

    private static string CollapseMunicipalityNoise(string value)
    {
        var result = value;
        foreach (var pair in MunicipalityShortNames)
        {
            result = Regex.Replace(
                result,
                $"^{Regex.Escape(pair.Key)}\\s*{Regex.Escape(pair.Value)}",
                pair.Value,
                RegexOptions.IgnoreCase);

            result = Regex.Replace(
                result,
                $"^{Regex.Escape(pair.Value)}\\s*{Regex.Escape(pair.Value)}",
                pair.Value,
                RegexOptions.IgnoreCase);
        }

        return result;
    }

    private static bool TryParseProvince(string text, out string province, out int consumed)
    {
        foreach (var candidate in ProvinceAliases)
        {
            if (!text.StartsWith(candidate.Alias, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!candidate.IsCanonical)
            {
                var remaining = text[candidate.Alias.Length..];
                if (MunicipalityRegions.Contains(candidate.Province))
                {
                    if (!CanParseMunicipalityAliasTail(remaining))
                    {
                        continue;
                    }
                }
                else if (!TryParseCity(remaining, candidate.Province, out _, out _, out _))
                {
                    continue;
                }
            }

            province = candidate.Province;
            consumed = candidate.Alias.Length;
            return true;
        }

        var match = GenericProvinceRegex.Match(text);
        if (match.Success)
        {
            province = match.Groups["province"].Value;
            consumed = province.Length;
            return true;
        }

        province = string.Empty;
        consumed = 0;
        return false;
    }

    private static bool TryParseCity(string text, string? provinceHint, out string province, out string city, out int consumed)
    {
        foreach (var candidate in CityAliases)
        {
            if (!string.IsNullOrWhiteSpace(provinceHint) &&
                !string.Equals(candidate.Province, provinceHint, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!text.StartsWith(candidate.Alias, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!candidate.IsCanonical && !CanParseAreaAtStart(text[candidate.Alias.Length..]))
            {
                continue;
            }

            province = candidate.Province;
            city = candidate.City;
            consumed = candidate.Alias.Length;
            return true;
        }

        province = string.Empty;
        city = string.Empty;
        consumed = 0;
        return false;
    }

    private static bool CanParseMunicipalityAliasTail(string text)
    {
        if (CanParseAreaAtStart(text))
        {
            return true;
        }

        var connectorLength = ConsumeMunicipalityConnector(text, 0);
        return connectorLength > 0 && CanParseAreaAtStart(text[connectorLength..]);
    }

    private static bool CanParseAreaAtStart(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return GenericAreaRegex.Match(text).Success;
    }

    private static int ConsumeMunicipalityConnector(string text, int offset)
    {
        if (offset >= text.Length)
        {
            return 0;
        }

        var remaining = text[offset..];
        foreach (var token in MunicipalityConnectorTokens)
        {
            if (remaining.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                return token.Length;
            }
        }

        return 0;
    }

    private static string ExtractDetail(string original, CompactAddress compact, int consumedCompactLength)
    {
        if (consumedCompactLength >= compact.Length)
        {
            return string.Empty;
        }

        var originalIndex = compact.OriginalIndexes[consumedCompactLength];
        var detail = original[originalIndex..].Trim();
        return detail.TrimStart('：', ':', '，', ',', '；', ';', '/', '\\', '-', '_', ' ');
    }

    private static CompactAddress BuildCompactAddress(string value)
    {
        var originalIndexes = new List<int>(value.Length);
        var buffer = new char[value.Length];
        var length = 0;

        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsWhiteSpace(value[i]))
            {
                continue;
            }

            buffer[length] = value[i];
            originalIndexes.Add(i);
            length++;
        }

        return new CompactAddress(new string(buffer, 0, length), originalIndexes.ToArray());
    }

    private static int CompareRegionCompleteness(AddressParts left, AddressParts right)
    {
        var leftScore = CountResolvedRegionSegments(left);
        var rightScore = CountResolvedRegionSegments(right);
        if (leftScore != rightScore)
        {
            return leftScore.CompareTo(rightScore);
        }

        var leftLength = CombineRegion(left.State, left.City, left.District).Length;
        var rightLength = CombineRegion(right.State, right.City, right.District).Length;
        return leftLength.CompareTo(rightLength);
    }

    private static int CountResolvedRegionSegments(AddressParts parts)
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(parts.State))
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(parts.City))
        {
            count++;
        }

        if (!string.IsNullOrWhiteSpace(parts.District))
        {
            count++;
        }

        return count;
    }

    private static ProvinceAliasEntry[] BuildProvinceAliases()
    {
        var aliases = new List<ProvinceAliasEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var province in ProvinceLevelRegions)
        {
            AddAlias(province, province, isCanonical: true);
            foreach (var alias in ExpandAliases(province, ProvinceAliasSuffixes))
            {
                AddAlias(province, alias, isCanonical: false);
            }
        }

        return aliases
            .OrderByDescending(static item => item.Alias.Length)
            .ToArray();

        void AddAlias(string province, string alias, bool isCanonical)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return;
            }

            if (!seen.Add($"{province}|{alias}"))
            {
                return;
            }

            aliases.Add(new ProvinceAliasEntry(province, alias, isCanonical));
        }
    }

    private static CityAliasEntry[] BuildCityAliases()
    {
        var aliases = new List<CityAliasEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cityRegion in CityLevelRegions)
        {
            AddAlias(cityRegion.Province, cityRegion.City, cityRegion.City, isCanonical: true);
            foreach (var alias in ExpandAliases(cityRegion.City, CityAliasSuffixes))
            {
                AddAlias(cityRegion.Province, cityRegion.City, alias, isCanonical: false);
            }
        }

        return aliases
            .OrderByDescending(static item => item.Alias.Length)
            .ToArray();

        void AddAlias(string province, string city, string alias, bool isCanonical)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return;
            }

            if (!seen.Add($"{province}|{city}|{alias}"))
            {
                return;
            }

            aliases.Add(new CityAliasEntry(province, city, alias, isCanonical));
        }
    }

    private static IEnumerable<string> ExpandAliases(string canonical, IEnumerable<string> suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (canonical.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                yield return canonical[..^suffix.Length];
            }
        }
    }

    private readonly record struct CompactAddress(string Text, int[] OriginalIndexes)
    {
        public int Length => Text.Length;
    }

    private readonly record struct ProvinceAliasEntry(string Province, string Alias, bool IsCanonical);
    private readonly record struct CityRegionEntry(string Province, string City);
    private readonly record struct CityAliasEntry(string Province, string City, string Alias, bool IsCanonical);
}

internal readonly record struct AddressParts(string State, string City, string District, string Detail)
{
    public static AddressParts Empty => new(string.Empty, string.Empty, string.Empty, string.Empty);
}
