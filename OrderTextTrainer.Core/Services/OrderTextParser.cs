using System.Text.RegularExpressions;
using OrderTextTrainer.Core.Models;

namespace OrderTextTrainer.Core.Services;

public sealed class OrderTextParser
{
    private static readonly Regex PhoneRegex = new(@"(?:\+?86[- ]?)?((?:1[3-9]\d[- ]?\d{4}[- ]?\d{4})|(?:1[3-9]\d{9}))(?:[-转 ](\d{1,6}))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LandlineRegex = new(@"(?<!\d)(0\d{2,3}-?\d{7,8})(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExplicitFieldRegex = new(@"^(?<label>[^:：]{1,8})[:：]\s*(?<value>.+)$", RegexOptions.Compiled);
    private static readonly Regex SeparatorRegex = new(@"^[-=_*]{4,}$", RegexOptions.Compiled);
    private static readonly Regex BracketNoiseRegex = new(@"\[(?:\d{3,}|号码保护中[^\]]*)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] AccessoryKeywords =
    {
        "护理液", "护理盒", "伴侣盒", "盒子", "镜盒", "双联盒", "收纳盒", "眼镜盒",
        "吸棒", "镊子", "湿巾", "润眼液", "清洗器", "工具盒"
    };
    private static readonly string[] ItemNoiseKeywords =
    {
        "lenspop", "leea", "清仓", "现货", "官网直发", "调货一群", "调货", "预售", "补发",
        "加急", "急发", "速发", "赠品", "赠送", "赠", "活动", "拍下备注", "备注下单"
    };
    private static readonly string[] VariantSuffixes =
    {
        "深蓝", "浅蓝", "蓝绿", "蓝灰", "灰粉", "灰蓝", "粉紫", "玫紫", "玫红", "金棕",
        "茶棕", "橘棕", "酒红", "深灰", "浅灰", "紫", "蓝", "青", "灰", "粉", "黄",
        "绿", "棕", "红", "黑", "白", "金", "银", "橙"
    };

    private readonly TextNormalizer _normalizer = new();

    public ParseResult Parse(string? rawText, ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries = null)
    {
        var result = new ParseResult();
        foreach (var _ in ParseOrders(rawText, ruleSet, catalogEntries, result))
        {
        }

        return result;
    }

    public IEnumerable<ParsedOrder> ParseOrders(
        string? rawText,
        ParserRuleSet ruleSet,
        IReadOnlyList<ProductCatalogEntry>? catalogEntries = null,
        ParseResult? parseResult = null)
    {
        var normalized = _normalizer.Normalize(rawText);
        parseResult ??= new ParseResult();
        parseResult.NormalizedText = normalized;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            parseResult.Warnings.Add("输入为空。");
            yield break;
        }

        var parseIndex = ParseIndex.Create(ruleSet, catalogEntries);
        foreach (var block in SplitIntoOrderBlocks(normalized, ruleSet))
        {
            var order = ParseSingleOrder(block, ruleSet, parseIndex, parseResult.UnknownSegments);
            if (order.Items.Count == 0)
            {
                parseResult.Warnings.Add($"有一段文本未识别出商品：{TrimForHint(block)}");
            }

            parseResult.Orders.Add(order);
            yield return order;
        }
    }

    public void AddOrUpdateProductAlias(ParserRuleSet ruleSet, string canonicalName, IEnumerable<string> aliases)
    {
        var normalizedCanonical = _normalizer.Normalize(canonicalName);
        if (string.IsNullOrWhiteSpace(normalizedCanonical))
        {
            return;
        }

        var aliasList = aliases
            .Append(normalizedCanonical)
            .Select(_normalizer.Normalize)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matchedRules = ruleSet.ProductAliases
            .Where(rule =>
                string.Equals(rule.CanonicalName, normalizedCanonical, StringComparison.OrdinalIgnoreCase) ||
                rule.Aliases.Any(alias => aliasList.Contains(alias, StringComparer.OrdinalIgnoreCase)) ||
                aliasList.Contains(rule.CanonicalName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (matchedRules.Count == 0)
        {
            ruleSet.ProductAliases.Add(new ProductAliasRule
            {
                CanonicalName = normalizedCanonical,
                Aliases = aliasList
            });
            return;
        }

        var primaryRule = matchedRules[0];
        primaryRule.CanonicalName = normalizedCanonical;
        primaryRule.Aliases = matchedRules
            .SelectMany(rule => rule.Aliases)
            .Concat(aliasList)
            .Append(normalizedCanonical)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var duplicateRule in matchedRules.Skip(1))
        {
            ruleSet.ProductAliases.Remove(duplicateRule);
        }
    }

    private List<string> SplitIntoOrderBlocks(string text, ParserRuleSet ruleSet)
    {
        var preBlocks = new List<string>();
        var currentBlock = new List<string>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || SeparatorRegex.IsMatch(CleanupFreeText(line)))
            {
                if (currentBlock.Count > 0)
                {
                    preBlocks.Add(string.Join('\n', currentBlock).Trim());
                    currentBlock.Clear();
                }

                continue;
            }

            currentBlock.Add(rawLine);
        }

        if (currentBlock.Count > 0)
        {
            preBlocks.Add(string.Join('\n', currentBlock).Trim());
        }

        preBlocks = MergeStandaloneNoiseBlocks(preBlocks);
        preBlocks = MergeAssociatedBlocks(preBlocks, ruleSet);
        if (preBlocks.Count > 1)
        {
            return preBlocks;
        }

        if (preBlocks.Count == 1)
        {
            var singleBlock = preBlocks[0];
            var singleBlockPhoneMatches = PhoneRegex.Matches(singleBlock);
            if (singleBlockPhoneMatches.Count <= 1)
            {
                return preBlocks;
            }

            return SplitByPhoneBoundaries(singleBlock);
        }

        var phoneMatches = PhoneRegex.Matches(text);
        if (phoneMatches.Count <= 1)
        {
            return new List<string> { text };
        }

        return SplitByPhoneBoundaries(text);
    }

    private static List<string> SplitByPhoneBoundaries(string text)
    {
        var phoneMatches = PhoneRegex.Matches(text);
        if (phoneMatches.Count <= 1)
        {
            return new List<string> { text };
        }

        var boundaries = new List<int> { 0 };
        for (var index = 1; index < phoneMatches.Count; index++)
        {
            var boundary = FindBoundaryBefore(text, phoneMatches[index].Index);
            if (boundary > boundaries[^1] + 20)
            {
                boundaries.Add(boundary);
            }
        }

        boundaries.Add(text.Length);
        var blocks = new List<string>();
        for (var index = 0; index < boundaries.Count - 1; index++)
        {
            var start = boundaries[index];
            var length = boundaries[index + 1] - start;
            if (length <= 0)
            {
                continue;
            }

            var block = text.Substring(start, length).Trim();
            if (!string.IsNullOrWhiteSpace(block))
            {
                blocks.Add(block);
            }
        }

        return blocks.Count == 0 ? new List<string> { text } : blocks;
    }

    private static List<string> MergeStandaloneNoiseBlocks(List<string> blocks)
    {
        if (blocks.Count <= 1)
        {
            return blocks;
        }

        var merged = new List<string>();
        foreach (var block in blocks)
        {
            if (IsStandaloneNoiseBlock(block))
            {
                if (merged.Count > 0)
                {
                    merged[^1] = $"{merged[^1]}\n{block}".Trim();
                }

                continue;
            }

            merged.Add(block);
        }

        return merged;
    }

    private static List<string> MergeAssociatedBlocks(List<string> blocks, ParserRuleSet ruleSet)
    {
        if (blocks.Count <= 1)
        {
            return blocks;
        }

        var merged = new List<string>();
        var current = new List<string>();
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block))
            {
                continue;
            }

            current.Add(block);
            if (BlockEndsOrder(block, ruleSet))
            {
                merged.Add(string.Join('\n', current).Trim());
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            merged.Add(string.Join('\n', current).Trim());
        }

        return merged;
    }

    private static bool BlockEndsOrder(string block, ParserRuleSet ruleSet)
    {
        return BlockContainsPhone(block) || IsStandaloneContactBlock(block, ruleSet);
    }

    private static bool BlockContainsPhone(string block)
    {
        if (string.IsNullOrWhiteSpace(block))
        {
            return false;
        }

        var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Any(line => PhoneRegex.IsMatch(CleanupFreeText(line)) || LandlineRegex.IsMatch(CleanupFreeText(line)));
    }

    private static bool IsStandaloneNoiseBlock(string block)
    {
        if (string.IsNullOrWhiteSpace(block))
        {
            return true;
        }

        var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 3)
        {
            return false;
        }

        var hasPhone = lines.Any(line => PhoneRegex.IsMatch(CleanupFreeText(line)) || LandlineRegex.IsMatch(CleanupFreeText(line)));
        if (hasPhone)
        {
            return false;
        }

        var hasAddress = lines.Any(line => LooksLikeAddressLikeLine(line, ParserRuleSet.CreateDefault()));
        if (hasAddress)
        {
            return false;
        }

        var hasProductLikeLine = lines.Any(line => LooksLikeProductCandidate(line) || TryDetectStandalonePowerHeading(line, out _));
        if (hasProductLikeLine)
        {
            return false;
        }

        return lines.Length == 1 &&
               (Regex.IsMatch(lines[0], @"^\d+$") ||
                lines[0].Contains("缺货", StringComparison.OrdinalIgnoreCase) ||
                lines[0].Contains("备注", StringComparison.OrdinalIgnoreCase) ||
                lines[0].Contains("售后", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStandaloneContactBlock(string block, ParserRuleSet ruleSet)
    {
        if (string.IsNullOrWhiteSpace(block))
        {
            return false;
        }

        var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || lines.Length > 4)
        {
            return false;
        }

        if (lines.Any(line => LooksLikeProductCandidate(line) || TryDetectStandalonePowerHeading(line, out _)))
        {
            return false;
        }

        if (!lines.Any(line => PhoneRegex.IsMatch(CleanupFreeText(line)) || LandlineRegex.IsMatch(CleanupFreeText(line))))
        {
            return false;
        }

        return lines.Any(line =>
            TryExtractContactLineContext(line, ruleSet, out _, out _) ||
            LooksLikeAddressLikeLine(line, ruleSet) ||
            IsRegionAddressLabel(line) ||
            IsDetailAddressLabel(line) ||
            line.Contains("收件", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("收货", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("地址", StringComparison.OrdinalIgnoreCase));
    }

    private static int FindBoundaryBefore(string text, int index)
    {
        for (var cursor = index; cursor >= 0; cursor--)
        {
            if (text[cursor] == '\n' && cursor + 1 < text.Length)
            {
                return cursor + 1;
            }
        }

        return 0;
    }

    private ParsedOrder ParseSingleOrder(string block, ParserRuleSet ruleSet, ParseIndex parseIndex, ICollection<string> unknownSegments)
    {
        var order = new ParsedOrder { SourceText = block };
        var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var consumedLines = new HashSet<int>();

        order.Brand = FindCanonicalAlias(block, parseIndex.BrandAliases);
        order.DetectedWearPeriod = FindCanonicalAlias(block, parseIndex.WearAliases);
        order.WearPeriod = order.DetectedWearPeriod;
        order.Phone = ExtractPhone(block);

        ExtractExplicitFields(lines, ruleSet, order, consumedLines);

        if (string.IsNullOrWhiteSpace(order.Address))
        {
            order.Address = GuessAddress(lines, ruleSet, consumedLines);
        }

        ApplyContactLineContext(lines, ruleSet, order);

        if (string.IsNullOrWhiteSpace(order.CustomerName) ||
            LooksLikeContactLabelToken(order.CustomerName) ||
            LooksLikeOrderMetaToken(order.CustomerName))
        {
            var recoveredName = RecoverReceiverNameFromLines(lines, ruleSet);
            if (!string.IsNullOrWhiteSpace(recoveredName))
            {
                order.CustomerName = recoveredName;
            }
        }

        if (string.IsNullOrWhiteSpace(order.CustomerName))
        {
            order.CustomerName = GuessCustomerName(lines, ruleSet, order.Phone, order.Address, consumedLines);
        }

        ParseItems(lines, ruleSet, parseIndex, order, consumedLines, unknownSegments);
        if (order.Items.Count == 0 && ContainsItemLikeClues(block))
        {
            var fallbackPower = DetectOrderWidePower(block);
            var fallbackLines = lines
                .Where(line => !SeparatorRegex.IsMatch(CleanupFreeText(line)) &&
                               ContainsItemLikeClues(line) &&
                               !IsIgnorableOrderMetadataLine(line, parseIndex))
                .OrderByDescending(line => CleanupFreeText(line).Length)
                .ToList();

            foreach (var fallbackLine in fallbackLines)
            {
                var fallbackItems = TryParseItems(fallbackLine, parseIndex, fallbackPower);
                if (fallbackItems.Count == 0)
                {
                    continue;
                }

                order.Items.AddRange(fallbackItems);
                break;
            }
        }

        ApplyOrderWidePowerContext(order, DetectOrderWidePower(block));
        ApplyImplicitZeroPowerContext(order);
        MergeOrderRemarkAdditions(order);

        if (order.Items.Count == 0 && ContainsItemLikeClues(block))
        {
            if (!RecordUnknownProductSegments(block, unknownSegments))
            {
                AddUnknownSegment(unknownSegments, CleanupFreeText(block));
            }
        }

        return order;
    }

    private void ExtractExplicitFields(List<string> lines, ParserRuleSet ruleSet, ParsedOrder order, ISet<int> consumedLines)
    {
        string? explicitAddressRegion = null;
        string? explicitAddressDetail = null;

        for (var index = 0; index < lines.Count; index++)
        {
            var match = ExplicitFieldRegex.Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            var label = match.Groups["label"].Value.Trim();
            var value = CleanupFreeText(match.Groups["value"].Value.Trim());
            if (ApplyExplicitField(label, value, ruleSet, order, ref explicitAddressRegion, ref explicitAddressDetail))
            {
                consumedLines.Add(index);
                ProcessEmbeddedExplicitFields(value, ruleSet, order, ref explicitAddressRegion, ref explicitAddressDetail);
                continue;
            }
        }

        var composedAddress = ComposeExplicitAddress(explicitAddressRegion, explicitAddressDetail);
        if (!string.IsNullOrWhiteSpace(composedAddress))
        {
            order.Address = composedAddress;
        }
    }

    private static bool IsLikelyNameFieldLabel(string label, ParserRuleSet ruleSet)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        if (Regex.IsMatch(label, @"(?:备注|留言|附言|说明|信息|内容)", RegexOptions.IgnoreCase))
        {
            return false;
        }

        return ruleSet.NameLabels.Any(item => label.Contains(item, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOrderMetaLabel(string label)
    {
        return Regex.IsMatch(label, @"(?:下单|订单号|订单编号|商品信息|商品|款式|品牌)", RegexOptions.IgnoreCase);
    }

    private static bool ApplyExplicitField(
        string label,
        string value,
        ParserRuleSet ruleSet,
        ParsedOrder order,
        ref string? explicitAddressRegion,
        ref string? explicitAddressDetail)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        if (IsLikelyNameFieldLabel(label, ruleSet) || Regex.IsMatch(label, @"(?:收件人|收貨人)", RegexOptions.IgnoreCase))
        {
            order.CustomerName = ExtractNameFromLabeledValue(value, ruleSet) ?? SanitizeName(value);
            return true;
        }

        if (ruleSet.PhoneLabels.Any(item => label.Contains(item, StringComparison.OrdinalIgnoreCase)) ||
            Regex.IsMatch(label, @"(?:聯系電話|联系电话|電話|电话)", RegexOptions.IgnoreCase))
        {
            order.Phone = ExtractPhone(value) ?? value;
            return true;
        }

        if (ruleSet.AddressLabels.Any(item => label.Contains(item, StringComparison.OrdinalIgnoreCase)) ||
            Regex.IsMatch(label, @"(?:收貨地址|收货地址|詳細地址|详细地址|所在地區|所在地区|地址)", RegexOptions.IgnoreCase))
        {
            if (IsRegionAddressLabel(label))
            {
                explicitAddressRegion = value;
            }
            else
            {
                explicitAddressDetail = value;
            }

            return true;
        }

        if (IsRegionAddressLabel(label))
        {
            explicitAddressRegion = value;
            return true;
        }

        if (IsDetailAddressLabel(label))
        {
            explicitAddressDetail = value;
            return true;
        }

        if (Regex.IsMatch(label, @"(?:邮政编码|郵政編[號号]|邮编)", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (label.Contains("赠", StringComparison.OrdinalIgnoreCase))
        {
            order.Gifts.Add(value);
            return true;
        }

        if (label.Contains("数量", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (label.Contains("品牌", StringComparison.OrdinalIgnoreCase) && LooksLikeGenericWearHeader(value))
        {
            return true;
        }

        if (label.Contains("备注", StringComparison.OrdinalIgnoreCase) && !ContainsItemLikeClues(value))
        {
            order.Remark = value;
            return true;
        }

        return IsOrderMetaLabel(label) && !ContainsItemLikeClues(value);
    }

    private static bool TryExtractEmbeddedExplicitFields(
        string value,
        ParserRuleSet ruleSet,
        ParsedOrder order,
        ref string? explicitAddressRegion,
        ref string? explicitAddressDetail)
    {
        var matches = Regex.Matches(value, @"(?<label>[^:：]{1,8})[:：]\s*(?<value>[^,，;；]+)", RegexOptions.IgnoreCase);
        var handled = false;
        foreach (Match match in matches)
        {
            var nestedLabel = match.Groups["label"].Value.Trim();
            var nestedValue = CleanupFreeText(match.Groups["value"].Value.Trim());
            handled |= ApplyExplicitField(nestedLabel, nestedValue, ruleSet, order, ref explicitAddressRegion, ref explicitAddressDetail);
        }

        return handled;
    }

    private static void ProcessEmbeddedExplicitFields(
        string value,
        ParserRuleSet ruleSet,
        ParsedOrder order,
        ref string? explicitAddressRegion,
        ref string? explicitAddressDetail)
    {
        TryExtractEmbeddedExplicitFields(value, ruleSet, order, ref explicitAddressRegion, ref explicitAddressDetail);
    }

    private static string? ExtractNameFromLabeledValue(string value, ParserRuleSet ruleSet)
    {
        var cleaned = CleanupFreeText(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        if (PhoneRegex.IsMatch(cleaned))
        {
            var phoneMatch = PhoneRegex.Match(cleaned);
            if (phoneMatch.Success && phoneMatch.Index > 0)
            {
                var beforePhone = cleaned[..phoneMatch.Index];
                var token = beforePhone.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(SanitizeName)
                    .LastOrDefault(candidate => (IsPossibleName(candidate, ruleSet) || LooksLikeReceiverNameWithNumericSuffix(candidate)));
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }
        }

        var fallback = cleaned.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeName)
            .FirstOrDefault(candidate => IsPossibleName(candidate, ruleSet) || LooksLikeReceiverNameWithNumericSuffix(candidate));
        return fallback;
    }

    private string? GuessAddress(List<string> lines, ParserRuleSet ruleSet, ISet<int> consumedLines)
    {
        foreach (var line in lines)
        {
            if (TryExtractContactLineContext(line, ruleSet, out _, out var addressFromContact) &&
                !string.IsNullOrWhiteSpace(addressFromContact))
            {
                return addressFromContact;
            }
        }

        string? bestLine = null;
        var bestScore = 0;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = CleanupFreeText(lines[index]);
            var score = ruleSet.AddressKeywords.Count(keyword => line.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(ExtractPhone(line)))
            {
                score += 1;
            }

            if (Regex.IsMatch(line, @"(?:北京|上海|天津|重庆|.+省|.+自治区|.+特别行政区).+(?:市|州|盟|地区).+(?:区|县|旗)", RegexOptions.IgnoreCase))
            {
                score += 2;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestLine = line;
            }
        }

        if (string.IsNullOrWhiteSpace(bestLine) || bestScore == 0)
        {
            return null;
        }

        var address = CleanupAddress(bestLine);
        for (var index = 0; index < lines.Count; index++)
        {
            if (string.Equals(CleanupFreeText(lines[index]), bestLine, StringComparison.Ordinal))
            {
                consumedLines.Add(index);
                break;
            }
        }

        return string.IsNullOrWhiteSpace(address) ? bestLine : address;
    }

    private string? GuessCustomerName(List<string> lines, ParserRuleSet ruleSet, string? phone, string? address, ISet<int> consumedLines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            if (TryExtractNameBeforeAddressDelimiter(line, ruleSet, out var prefixedName))
            {
                consumedLines.Add(index);
                return prefixedName;
            }

            if (TryExtractContactLineContext(line, ruleSet, out var nameFromContact, out _) &&
                !string.IsNullOrWhiteSpace(nameFromContact))
            {
                consumedLines.Add(index);
                return nameFromContact;
            }

            if (TryExtractLooseContactLabelName(line, ruleSet, out var looseName))
            {
                consumedLines.Add(index);
                return looseName;
            }

            var cleanedLine = CleanupFreeText(line);
            if (ruleSet.NoiseKeywords.Any(keyword => cleanedLine.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var nameFromAddressLine = TryExtractNameFromAddressLikeLine(cleanedLine, ruleSet);
            if (!string.IsNullOrWhiteSpace(nameFromAddressLine))
            {
                consumedLines.Add(index);
                return nameFromAddressLine;
            }

            if (LooksLikeProductCandidate(cleanedLine) || TryDetectStandalonePowerHeading(cleanedLine, out _))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(phone) && PhoneRegex.IsMatch(cleanedLine))
            {
                var phoneMatch = PhoneRegex.Match(cleanedLine);
                if (phoneMatch.Success && phoneMatch.Index > 0)
                {
                    var beforePhone = cleanedLine[..phoneMatch.Index];
                    var leadingToken = beforePhone.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(SanitizeName)
                        .LastOrDefault(candidate => IsPossibleName(candidate, ruleSet) && !LooksLikeContactLabelToken(candidate));
                    if (!string.IsNullOrWhiteSpace(leadingToken))
                    {
                        consumedLines.Add(index);
                        return leadingToken;
                    }
                }

                var compact = PhoneRegex.Replace(cleanedLine, " ");
                if (!string.IsNullOrWhiteSpace(address))
                {
                    compact = compact.Replace(address, " ", StringComparison.OrdinalIgnoreCase);
                }

                var token = compact.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(SanitizeName)
                    .LastOrDefault(candidate => IsPossibleName(candidate, ruleSet) && !LooksLikeContactLabelToken(candidate));
                if (!string.IsNullOrWhiteSpace(token))
                {
                    consumedLines.Add(index);
                    return token;
                }
            }
        }

        for (var index = 0; index < lines.Count; index++)
        {
            if (consumedLines.Contains(index))
            {
                continue;
            }

            var line = SanitizeName(lines[index]);
            if (LooksLikeProductCandidate(line) || TryDetectStandalonePowerHeading(line, out _))
            {
                continue;
            }

            if (!LooksLikeOrderMetaToken(line) &&
                IsPossibleName(line, ruleSet) &&
                (string.IsNullOrWhiteSpace(phone) || LooksLikeStrongFallbackName(line, ruleSet)))
            {
                consumedLines.Add(index);
                return line;
            }
        }

        return null;
    }

    private static bool TryExtractNameBeforeAddressDelimiter(string line, ParserRuleSet ruleSet, out string? name)
    {
        name = null;

        var cleanedLine = CleanupFreeText(line);
        if (string.IsNullOrWhiteSpace(cleanedLine))
        {
            return false;
        }

        var match = Regex.Match(
            cleanedLine,
            @"(?<name>[\p{IsCJKUnifiedIdeographs}A-Za-z]{1,8})\s*(?:[\/／\\\-]\s*)(?<address>.+)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        var candidate = SanitizeName(match.Groups["name"].Value);
        if (!IsPossibleName(candidate, ruleSet) || LooksLikeOrderMetaToken(candidate))
        {
            return false;
        }

        var addressPart = CleanupFreeText(match.Groups["address"].Value);
        if (!LooksLikeAddressLikeLine(addressPart, ruleSet) &&
            !Regex.IsMatch(addressPart, @"(?:北京|上海|天津|重庆|[\p{IsCJKUnifiedIdeographs}]{2,}(?:省|市|自治区|特别行政区)).*(?:区|县|镇|乡|街道|大道|路|号|楼|单元|室|园|村|仓|驿站|校区|大厦|家园)", RegexOptions.IgnoreCase))
        {
            return false;
        }

        name = candidate;
        return true;
    }

    private static bool TryExtractLooseContactLabelName(string line, ParserRuleSet ruleSet, out string? name)
    {
        name = null;

        var cleanedLine = CleanupFreeText(line);
        if (string.IsNullOrWhiteSpace(cleanedLine))
        {
            return false;
        }

        var match = Regex.Match(
            cleanedLine,
            @"^(?:姓名|名字|收件人|收货人|客户)\s+([\p{IsCJKUnifiedIdeographs}A-Za-z]{1,8})(?:\s+.*)?$",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        var candidate = SanitizeName(match.Groups[1].Value);
        if (!IsPossibleName(candidate, ruleSet) || LooksLikeOrderMetaToken(candidate))
        {
            return false;
        }

        name = candidate;
        return true;
    }

    private static string? RecoverReceiverNameFromLines(List<string> lines, ParserRuleSet ruleSet)
    {
        foreach (var line in lines)
        {
            var cleanedLine = CleanupFreeText(line);
            if (string.IsNullOrWhiteSpace(cleanedLine))
            {
                continue;
            }

            var directMatch = Regex.Match(
                cleanedLine,
                @"^(?:收件人|收货人|姓名|名字|客户)\s*[:：]?\s*(?<name>[\p{IsCJKUnifiedIdeographs}A-Za-z]{1,8})(?=$|[\s,，;；/／\\-])",
                RegexOptions.IgnoreCase);
            if (directMatch.Success)
            {
                var directCandidate = SanitizeName(directMatch.Groups["name"].Value);
                if ((IsPossibleName(directCandidate, ruleSet) || LooksLikeReceiverNameWithNumericSuffix(directCandidate)) &&
                    !LooksLikeContactLabelToken(directCandidate) &&
                    !LooksLikeOrderMetaToken(directCandidate))
                {
                    return directCandidate;
                }
            }

            var match = Regex.Match(
                cleanedLine,
                @"^(?:收件人|收货人|姓名|名字|客户)\s*[:：]?\s*(?<value>.+)$",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                continue;
            }

            var value = match.Groups["value"].Value;
            var candidate = ExtractNameFromLabeledValue(value, ruleSet);
            candidate ??= value.Split(new[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(SanitizeName)
                .FirstOrDefault(token =>
                    (IsPossibleName(token, ruleSet) || LooksLikeReceiverNameWithNumericSuffix(token)) &&
                    !LooksLikeContactLabelToken(token) &&
                    !LooksLikeOrderMetaToken(token));

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void ParseItems(List<string> lines, ParserRuleSet ruleSet, ParseIndex parseIndex, ParsedOrder order, ISet<int> consumedLines, ICollection<string> unknownSegments)
    {
        var separatorIndex = FindItemSeparatorIndex(lines);
        var itemLines = separatorIndex >= 0 ? lines.Take(separatorIndex).ToList() : lines;
        var tailRemarkLines = separatorIndex >= 0 ? lines.Skip(separatorIndex + 1).ToList() : new List<string>();
        string? currentPower = null;
        string? currentWearPeriod = null;
        string? currentLinePower = null;
        var currentLineIndex = -1;
        var lineTrailingPowers = BuildLineTrailingPowerMap(itemLines, parseIndex);

        foreach (var tailLine in tailRemarkLines)
        {
            var cleanedTailLine = NormalizeSupplementRemark(tailLine);
            if (!string.IsNullOrWhiteSpace(cleanedTailLine))
            {
                AppendRemark(order, cleanedTailLine);
            }
        }

        var segments = ExpandSegments(itemLines)
            .SelectMany(segment => ExpandSlashEnumeratedVariantSegments(segment, parseIndex))
            .SelectMany(segment => ExpandEnumeratedVariantSegments(segment, parseIndex))
            .SelectMany(SplitLooseDelimitedSegments)
            .SelectMany(segment => SplitByKnownProducts(segment, parseIndex))
            .ToList();
        segments = MergeContinuationSegments(segments, parseIndex)
            .ToList();

        for (var index = 0; index < segments.Count; index++)
        {
            var trimmedSegment = CleanupFreeText(segments[index].Trim());
            if (string.IsNullOrWhiteSpace(trimmedSegment) || SeparatorRegex.IsMatch(trimmedSegment))
            {
                continue;
            }

            var parsingSegment = TrimTrailingContactTail(trimmedSegment);
            if (string.IsNullOrWhiteSpace(parsingSegment))
            {
                parsingSegment = trimmedSegment;
            }

            TryFindOriginalLineIndex(lines, trimmedSegment, out var lineIndex);
            if (lineIndex >= 0 && consumedLines.Contains(lineIndex))
            {
                continue;
            }

            if (lineIndex != currentLineIndex)
            {
                currentLineIndex = lineIndex;
                currentLinePower = null;
            }

            if (!ShouldAttemptItemParsing(parsingSegment, ruleSet, parseIndex, order))
            {
                continue;
            }

            if (ruleSet.GiftKeywords.Any(keyword => parsingSegment.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                order.Gifts.Add(parsingSegment);
                if (lineIndex >= 0)
                {
                    consumedLines.Add(lineIndex);
                }
                continue;
            }

            if (LooksLikeGiftOrAccessoryLine(parsingSegment, ruleSet, parseIndex))
            {
                order.Gifts.Add(parsingSegment);
                if (lineIndex >= 0)
                {
                    consumedLines.Add(lineIndex);
                }
                continue;
            }

            if (TryDetectStandalonePowerHeading(parsingSegment, out var headingPower))
            {
                currentPower = headingPower;
                continue;
            }

            if (TryDetectStandaloneWearPeriodHeading(parsingSegment, out var headingWearPeriod))
            {
                currentWearPeriod = headingWearPeriod;
                continue;
            }

            if (LooksLikeMetadata(parsingSegment, ruleSet, parseIndex, order))
            {
                continue;
            }

            var segmentPower = currentLinePower ?? currentPower;
            if (lineIndex >= 0 && lineTrailingPowers.TryGetValue(lineIndex, out var trailingPower))
            {
                segmentPower = trailingPower;
            }

            var parsedItems = TryParseItems(parsingSegment, parseIndex, segmentPower, currentWearPeriod);
            if (parsedItems.Count > 0)
            {
                foreach (var item in parsedItems)
                {
                    if (item.IsOutOfStock)
                    {
                        order.OutOfStockLines.Add(parsingSegment);
                        if (lineIndex >= 0)
                        {
                            consumedLines.Add(lineIndex);
                        }

                        order.Items.Add(item);
                        continue;
                    }

                    order.Items.Add(item);
                }

                var residualItems = TryParseResidualItems(parsingSegment, parsedItems, parseIndex, segmentPower, currentWearPeriod);
                foreach (var residualItem in residualItems)
                {
                    order.Items.Add(residualItem);
                }

                CaptureResidualUnknownSegments(parsingSegment, parsedItems, unknownSegments);

                if (TryDetectInlinePowerContext(parsingSegment, parsedItems, out var inlinePower))
                {
                    currentLinePower = inlinePower;
                }

                continue;
            }

            if (TryCaptureRemarkSegment(parsingSegment, order, lineIndex, consumedLines))
            {
                continue;
            }

            if (RecordUnknownProductSegments(parsingSegment, unknownSegments))
            {
                continue;
            }

            if (LooksLikeItemDetailFragment(parsingSegment))
            {
                AddUnknownSegment(unknownSegments, parsingSegment);
            }
        }

        if (order.Items.Count == 0 && segments.Count > 0)
        {
            var fallbackCandidates = segments
                .Select(CleanupFreeText)
                .Where(segment => !string.IsNullOrWhiteSpace(segment) &&
                                  IsSemanticallyProductLike(segment, ruleSet, parseIndex))
                .ToList();

            foreach (var fallbackCandidate in fallbackCandidates)
            {
                var fallbackItems = TryParseLooseSegmentItems(fallbackCandidate, ruleSet, parseIndex, currentPower, currentWearPeriod);
                if (fallbackItems.Count == 0)
                {
                    continue;
                }

                foreach (var item in fallbackItems)
                {
                    if (item.IsOutOfStock)
                    {
                        order.OutOfStockLines.Add(fallbackCandidate);
                    }

                    order.Items.Add(item);
                }
            }
        }
    }

    private List<OrderItem> TryParseLooseSegmentItems(string segment, ParserRuleSet ruleSet, ParseIndex parseIndex, string? currentPower = null, string? currentWearPeriod = null)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return new List<OrderItem>();
        }

        var parts = Regex.Split(cleaned, @"[\s,，;；]+")
            .Select(CleanupFreeText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (parts.Count == 0)
        {
            return new List<OrderItem>();
        }

        List<OrderItem>? bestItems = null;
        var bestScore = int.MinValue;

        for (var start = parts.Count - 1; start >= 0; start--)
        {
            var candidate = string.Join(" ", parts.Skip(start)).Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (!IsSemanticallyProductLike(candidate, ruleSet, parseIndex))
            {
                continue;
            }

            var parsedItems = TryParseItems(candidate, parseIndex, currentPower, currentWearPeriod);
            if (parsedItems.Count == 0)
            {
                continue;
            }

            var score = ScoreLooseParsedItems(parsedItems);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestItems = parsedItems;
        }

        return bestItems ?? new List<OrderItem>();
    }

    private static int ScoreLooseParsedItems(IReadOnlyCollection<OrderItem> parsedItems)
    {
        if (parsedItems.Count == 0)
        {
            return int.MinValue;
        }

        var item = parsedItems.First();
        var text = CleanupFreeText(item.ProductName ?? item.RawText);
        if (string.IsNullOrWhiteSpace(text))
        {
            return int.MinValue;
        }

        var leadingToken = Regex.Split(text, @"[\s,，;；]+")
            .Select(CleanupFreeText)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? text;

        var score = 0;
        if (LooksLikeGenericWearHeader(leadingToken))
        {
            return int.MinValue / 4;
        }

        score += Regex.Matches(leadingToken, @"(?:日抛|年抛|半年抛|月抛|季抛|试戴|试用|片装|副|盒|个|度|x|X)", RegexOptions.IgnoreCase).Count * 20;
        score += Regex.Matches(leadingToken, @"[\p{IsCJKUnifiedIdeographs}A-Za-z]{2,}", RegexOptions.IgnoreCase).Count * 6;
        score -= Regex.Matches(leadingToken, @"(?:省|市|区|县|镇|街|路|号|楼|室|@|#|/)", RegexOptions.IgnoreCase).Count * 20;
        score -= Regex.Matches(text, @"\d{4,}", RegexOptions.IgnoreCase).Count * 8;
        score -= leadingToken.Length;

        if (leadingToken.StartsWith("0度", StringComparison.OrdinalIgnoreCase) ||
            leadingToken.StartsWith("×", StringComparison.OrdinalIgnoreCase) ||
            leadingToken.StartsWith("x", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(leadingToken, @"^\d"))
        {
            score -= 50;
        }

        return score;
    }

    // Parsing priority:
    // 1. Explicit/metadata/address lines exit early.
    // 2. Known aliases or standalone semantic model names are preferred.
    // 3. Generic item clues are only used as fallback.
    private static bool ShouldAttemptItemParsing(string segment, ParserRuleSet ruleSet, ParseIndex parseIndex, ParsedOrder order)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        if (IsIgnorableOrderMetadataLine(cleaned, parseIndex))
        {
            return false;
        }

        if (IsSemanticallyProductLike(cleaned, ruleSet, parseIndex))
        {
            return true;
        }

        if (LooksLikeMetadata(cleaned, ruleSet, parseIndex, order))
        {
            return false;
        }

        return false;
    }

    private static bool IsSemanticallyProductLike(string segment, ParserRuleSet ruleSet, ParseIndex parseIndex)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        if (IsIgnorableOrderMetadataLine(cleaned, parseIndex))
        {
            return false;
        }

        if (PhoneRegex.IsMatch(cleaned) || LandlineRegex.IsMatch(cleaned))
        {
            return false;
        }

        var hasKnownAlias = ContainsKnownProductAlias(cleaned, parseIndex);
        var hasStandaloneModel = LooksLikeStandaloneModelName(cleaned);
        if (hasKnownAlias || hasStandaloneModel)
        {
            return true;
        }

        if (LooksLikeAddressLikeLine(cleaned, ruleSet))
        {
            return false;
        }

        return ContainsItemLikeClues(cleaned) || LooksLikeProductCandidate(cleaned);
    }

    private List<OrderItem> TryParseResidualItems(
        string segment,
        IReadOnlyCollection<OrderItem> parsedItems,
        ParseIndex parseIndex,
        string? currentPower,
        string? currentWearPeriod)
    {
        var originalResidual = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(originalResidual))
        {
            return new List<OrderItem>();
        }

        var residual = originalResidual;

        foreach (var productName in parsedItems
                     .Select(item => CleanupFreeText(item.ProductName))
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(value => value.Length))
        {
            residual = Regex.Replace(residual, Regex.Escape(productName), " ", RegexOptions.IgnoreCase);
        }

        residual = CleanupFreeText(residual);
        var looseResidualName = ExtractLooseProductName(residual) ?? residual;
        if (string.IsNullOrWhiteSpace(residual) ||
            string.Equals(residual, originalResidual, StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(residual, @"^[（(\[【]", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(residual, @"^(?:左眼|右眼)", RegexOptions.IgnoreCase) ||
            (!ContainsKnownProductAlias(residual, parseIndex) && !LooksLikeStandaloneModelName(looseResidualName)) ||
            LooksLikeItemDetailFragment(residual) ||
            IsIgnorableOrderMetadataLine(residual, parseIndex))
        {
            return new List<OrderItem>();
        }

        var parsedResidualItems = TryParseLooseSegmentItems(residual, ParserRuleSet.CreateDefault(), parseIndex, currentPower, currentWearPeriod);
        return parsedResidualItems
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductName))
            .Where(item => !LooksLikeAddressLikeLooseProductName(item.ProductName!))
            .ToList();
    }

    private static bool LooksLikeGenericWearHeader(string text)
    {
        var cleaned = CleanupFreeText(text);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        cleaned = Regex.Replace(
            cleaned,
            @"(?:lenspop|leea|日抛(?:\d+|[一二两三四五六七八九十])?片装?|日抛|年抛|半年抛|月抛|季抛|试戴片?|试用|新品竖瞳)",
            string.Empty,
            RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[\s,;，；:：_\-]+", string.Empty);

        return Regex.Matches(cleaned, @"[\p{IsCJKUnifiedIdeographs}A-Za-z]").Count < 2;
    }

    private static bool TryDetectStandaloneWearPeriodHeading(string text, out string wearPeriod)
    {
        wearPeriod = MatchStandaloneWearPeriodHeading(text);
        return !string.IsNullOrWhiteSpace(wearPeriod) && LooksLikeGenericWearHeader(text);
    }

    private static string MatchStandaloneWearPeriodHeading(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        if ((source.Contains("试戴", StringComparison.OrdinalIgnoreCase) ||
             source.Contains("试用", StringComparison.OrdinalIgnoreCase)) &&
            source.Contains("日抛", StringComparison.OrdinalIgnoreCase))
        {
            return "日抛2片";
        }

        if (ContainsExplicitTenPieceDailyCue(source))
        {
            return "日抛10片";
        }

        if (source.Contains("日抛2片", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("日抛两片", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("日抛", StringComparison.OrdinalIgnoreCase))
        {
            return "日抛2片";
        }

        if (source.Contains("半年抛", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("半抛", StringComparison.OrdinalIgnoreCase))
        {
            return "半年抛";
        }

        if (source.Contains("年抛", StringComparison.OrdinalIgnoreCase))
        {
            return "年抛";
        }

        if (source.Contains("试戴", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("试用", StringComparison.OrdinalIgnoreCase))
        {
            return "试戴片";
        }

        return string.Empty;
    }

    private static bool ContainsExplicitTenPieceDailyCue(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return source.Contains("日抛10片", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("日抛十片", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("日抛10片装", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("日抛十片装", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(source, @"(?:日抛|日拋)\s*(?:10片|十片|10片装|十片装)", RegexOptions.IgnoreCase);
    }

    private static int FindItemSeparatorIndex(IReadOnlyList<string> lines)
    {
        var seenContent = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var cleaned = CleanupFreeText(lines[index]);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                continue;
            }

            if (SeparatorRegex.IsMatch(cleaned))
            {
                if (seenContent)
                {
                    return index;
                }

                continue;
            }

            seenContent = true;
        }

        return -1;
    }

    private static Dictionary<int, string> BuildLineTrailingPowerMap(List<string> itemLines, ParseIndex parseIndex)
    {
        var result = new Dictionary<int, string>();
        for (var index = 0; index < itemLines.Count; index++)
        {
            var candidateSegments = SplitByKnownProducts(itemLines[index], parseIndex)
                .Select(CleanupFreeText)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            if (candidateSegments.Count > 1)
            {
                continue;
            }

            if (TryDetectLineTrailingPower(itemLines[index], parseIndex, out var trailingPower))
            {
                result[index] = trailingPower;
            }
        }

        return result;
    }

    private static bool RecordUnknownProductSegments(string segment, ICollection<string> unknownSegments)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        var parts = Regex.Split(cleaned, @"[、,，;；\s]+")
            .Select(CleanupFreeText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        var productLikeParts = parts
            .Where(LooksLikeUnknownProductFragment)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (productLikeParts.Count >= 1)
        {
            foreach (var part in productLikeParts)
            {
                AddUnknownSegment(unknownSegments, part);
            }

            return true;
        }

        if (parts.Count == 1 && LooksLikeUnknownProductFragment(cleaned))
        {
            AddUnknownSegment(unknownSegments, cleaned);
            return true;
        }

        return false;
    }

    private static bool LooksLikeUnknownProductFragment(string segment)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        if (LooksLikeStandaloneModelName(cleaned))
        {
            return true;
        }

        if (!Regex.IsMatch(cleaned, @"[\p{IsCJKUnifiedIdeographs}A-Za-z]{2,}"))
        {
            return false;
        }

        if (PhoneRegex.IsMatch(cleaned) || LandlineRegex.IsMatch(cleaned))
        {
            return false;
        }

        if (Regex.IsMatch(cleaned, @"^\d+(?:/\d+)?(?:\s*(?:副|盒|个|片))?$"))
        {
            return false;
        }

        if (Regex.IsMatch(cleaned, @"^\d{1,4}(?:/\d{1,4})?(?:\s*(?:一|二|两|三|四|五|六|七|八|九|十|\d+)?(?:副|盒|个|片))?$"))
        {
            return false;
        }

        if (Regex.IsMatch(cleaned, @"(?:半年抛|日抛|年抛|月抛|季抛|试戴|试用|lenspop|leea)", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(cleaned, @"(?:度|[xX×*＊]\d+|\d{1,4}|(?:蓝|红|棕|灰|紫|绿|青|粉|黄|橘|金|黑|白))", RegexOptions.IgnoreCase))
        {
            return false;
        }

        return Regex.IsMatch(cleaned, @"(?:度|[xX×*＊]\d+|\d{1,4}|(?:蓝|红|棕|灰|紫|绿|青|粉|黄|橘|金|黑|白))", RegexOptions.IgnoreCase);
    }

    private static void CaptureResidualUnknownSegments(string segment, IReadOnlyCollection<OrderItem> parsedItems, ICollection<string> unknownSegments)
    {
        var residual = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(residual))
        {
            return;
        }

        foreach (var productName in parsedItems
                     .Select(item => CleanupFreeText(item.ProductName))
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(value => value.Length))
        {
            residual = Regex.Replace(residual, Regex.Escape(productName), " ", RegexOptions.IgnoreCase);
        }

        RecordUnknownProductSegments(residual, unknownSegments);
    }

    private static void AddUnknownSegment(ICollection<string> unknownSegments, string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return;
        }

        if (!unknownSegments.Contains(segment))
        {
            unknownSegments.Add(segment);
        }
    }

    private static void MergeOrderRemarkAdditions(ParsedOrder order)
    {
        foreach (var gift in order.Gifts)
        {
            AppendRemark(order, NormalizeSupplementRemark(gift));
        }

        foreach (var outOfStockLine in order.OutOfStockLines)
        {
            AppendRemark(order, NormalizeSupplementRemark(outOfStockLine));
        }
    }

    private static string NormalizeSupplementRemark(string? text)
    {
        var cleaned = CleanupFreeText(text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        var phoneMatch = PhoneRegex.Match(cleaned);
        if (phoneMatch.Success && phoneMatch.Index > 0)
        {
            cleaned = cleaned[..phoneMatch.Index];
        }

        var addressMatch = Regex.Match(cleaned, @"(?:北京|上海|天津|重庆|[\p{IsCJKUnifiedIdeographs}]{2,}(?:省|市|自治区|特别行政区)).*(?:区|县|镇|乡|街道|大道|路|号|楼|单元|室|园|村|仓|驿站|校区|大厦|家园)", RegexOptions.IgnoreCase);
        if (addressMatch.Success && addressMatch.Index > 0)
        {
            cleaned = cleaned[..addressMatch.Index];
        }

        var tokens = cleaned
            .Split(new[] { '，', ',', '；', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (tokens.Count <= 1)
        {
            return cleaned.Trim(' ', ',', '，', ';', '；', ':', '：');
        }

        var keptTokens = new List<string>();
        foreach (var token in tokens)
        {
            if (keptTokens.Count == 0)
            {
                keptTokens.Add(token);
                continue;
            }

            if (AccessoryKeywords.Any(keyword => token.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                token.Contains("赠", StringComparison.OrdinalIgnoreCase) ||
                token.Contains("送", StringComparison.OrdinalIgnoreCase) ||
                token.Contains("缺货", StringComparison.OrdinalIgnoreCase) ||
                token.Contains("售后", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(token, @"\d"))
            {
                keptTokens.Add(token);
            }
            else
            {
                break;
            }
        }

        return string.Join('，', keptTokens)
            .Trim(' ', ',', '，', ';', '；', ':', '：');
    }

    private static bool TryCaptureRemarkSegment(string segment, ParsedOrder order, int lineIndex, ISet<int> consumedLines)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        var explicitRemark = Regex.Match(cleaned, @"^(备注|售后单|售后)\s*[:：]?\s*(.+)$", RegexOptions.IgnoreCase);
        if (explicitRemark.Success)
        {
            var remarkValue = explicitRemark.Groups[2].Value;
            if (LooksLikeItemDetailFragment(remarkValue))
            {
                return false;
            }

            AppendRemark(order, remarkValue);
            if (lineIndex >= 0)
            {
                consumedLines.Add(lineIndex);
            }

            return true;
        }

        if (cleaned.Contains("售后单", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Contains("售后", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Contains("缺货", StringComparison.OrdinalIgnoreCase))
        {
            AppendRemark(order, cleaned);
            if (lineIndex >= 0)
            {
                consumedLines.Add(lineIndex);
            }

            return true;
        }

        return false;
    }

    private static void AppendRemark(ParsedOrder order, string? remark)
    {
        var cleaned = CleanupFreeText(remark ?? string.Empty);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(order.Remark))
        {
            order.Remark = cleaned;
            return;
        }

        var parts = order.Remark
            .Split('；', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanupFreeText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (parts.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        order.Remark = string.Join('；', parts.Append(cleaned));
    }

    private static bool TryFindOriginalLineIndex(List<string> lines, string segment, out int lineIndex)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (CleanupFreeText(lines[index]).Contains(segment, StringComparison.OrdinalIgnoreCase))
            {
                lineIndex = index;
                return true;
            }
        }

        lineIndex = -1;
        return false;
    }

    private static IEnumerable<string> ExpandSegments(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            foreach (var part in line.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return part;
            }
        }
    }

    private static IEnumerable<string> SplitByKnownProducts(string segment, ParseIndex parseIndex)
    {
        var matches = parseIndex.KnownProductAliases
            .Select(alias => new
            {
                CanonicalName = alias,
                Alias = alias,
                Index = segment.IndexOf(alias, StringComparison.OrdinalIgnoreCase),
                alias.Length
            })
            .Where(item => item.Index >= 0)
            .OrderBy(item => item.Index)
            .ThenByDescending(item => item.Length)
            .ToList();

        if (matches.Count <= 1)
        {
            yield return segment;
            yield break;
        }

        var startPositions = new List<int>();
        var lastStart = -1;
        foreach (var match in matches)
        {
            if (match.Index == lastStart)
            {
                continue;
            }

            startPositions.Add(match.Index);
            lastStart = match.Index;
        }

        if (startPositions.Count <= 1)
        {
            yield return segment;
            yield break;
        }

        for (var index = 0; index < startPositions.Count; index++)
        {
            var start = startPositions[index];
            var end = index + 1 < startPositions.Count ? startPositions[index + 1] : segment.Length;
            var length = end - start;
            if (length <= 0)
            {
                continue;
            }

            var piece = segment.Substring(start, length).Trim(' ', ',', ';');
            if (!string.IsNullOrWhiteSpace(piece))
            {
                yield return piece;
            }
        }
    }

    private static IEnumerable<string> SplitLooseDelimitedSegments(string segment)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            yield break;
        }

        var parts = Regex.Split(cleaned, @"[、,，;；]+")
            .Select(CleanupFreeText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (parts.Count <= 1)
        {
            yield return cleaned;
            yield break;
        }

        var productLikeCount = parts.Count(LooksLikeProductCandidate);
        if (productLikeCount < 2)
        {
            yield return cleaned;
            yield break;
        }

        if (parts.Skip(1).Any(part => Regex.IsMatch(part, @"^\s*(?:\d{1,4}\s*度?|[xX×*＊]\s*\d+|0度)", RegexOptions.IgnoreCase)))
        {
            yield return cleaned;
            yield break;
        }

        foreach (var part in parts)
        {
            yield return part;
        }
    }

    private List<OrderItem> TryParseItems(string segment, ParseIndex parseIndex, string? currentPower = null, string? currentWearPeriod = null)
    {
        var normalized = CleanupFreeText(segment.Trim('"', ' '));
        if (LooksLikeGenericWearHeader(normalized))
        {
            return new List<OrderItem>();
        }

        var matchReadyText = BuildMatchReadyItemText(normalized);
        var productName = FindProductName(matchReadyText, parseIndex)
                          ?? FindProductName(normalized, parseIndex);
        if (string.IsNullOrWhiteSpace(productName))
        {
            if (!TryBuildLooseItem(normalized, currentPower, currentWearPeriod, out var looseItem))
            {
                return new List<OrderItem>();
            }

            return new List<OrderItem> { looseItem };
        }

        if (LooksLikeNonProductDetail(normalized, productName))
        {
            return new List<OrderItem>();
        }

        var powers = ExtractPowers(normalized, productName);
        if (powers.Count == 0 && !string.IsNullOrWhiteSpace(currentPower))
        {
            powers = new List<string> { currentPower };
        }
        var quantity = ExtractQuantity(normalized);
        var isTrial = normalized.Contains("试戴", StringComparison.OrdinalIgnoreCase) || normalized.Contains("试用", StringComparison.OrdinalIgnoreCase);
        var isOutOfStock = normalized.Contains("缺货", StringComparison.OrdinalIgnoreCase);
        var remark = ExtractItemRemark(normalized, isOutOfStock);

        if (TrySplitOralPowerItems(normalized, productName, isTrial, isOutOfStock, remark, currentWearPeriod, out var splitItems))
        {
            return splitItems;
        }

        var item = new OrderItem
        {
            RawText = normalized,
            ProductName = productName,
            Quantity = quantity,
            IsTrial = isTrial,
            IsOutOfStock = isOutOfStock,
            Remark = remark,
            LocalWearPeriodHint = currentWearPeriod
        };

        if (powers.Count >= 2)
        {
            if (string.Equals(powers[0], powers[1], StringComparison.OrdinalIgnoreCase))
            {
                item.LeftPower = powers[0];
                item.PowerSummary = powers[0];
                item.Quantity ??= 1;
            }
            else
            {
                item.LeftPower = powers[0];
                item.RightPower = powers[1];
                item.PowerSummary = $"{powers[0]}/{powers[1]}";
            }
        }
        else if (powers.Count == 1)
        {
            item.LeftPower = powers[0];
            item.PowerSummary = powers[0];
        }

        var items = new List<OrderItem> { item };
        items.AddRange(BuildTrailingPowerQuantityItems(normalized, productName, currentPower, currentWearPeriod, isTrial, isOutOfStock, remark));
        return items;
    }

    private static bool TryBuildLooseItem(string segment, string? currentPower, string? currentWearPeriod, out OrderItem item)
    {
        item = new OrderItem();

        if (string.IsNullOrWhiteSpace(segment) ||
            (!LooksLikeProductCandidate(segment) && !LooksLikeItemDetailFragment(segment)))
        {
            return false;
        }

        if (LooksLikeGenericWearHeader(segment))
        {
            return false;
        }

        var productName = ExtractLooseProductName(segment);
        if (string.IsNullOrWhiteSpace(productName) ||
            LooksLikeOrderMetaToken(productName) ||
            LooksLikeAddressLikeLooseProductName(productName) ||
            PhoneRegex.IsMatch(productName) ||
            LandlineRegex.IsMatch(productName))
        {
            return false;
        }

        var powers = ExtractPowers(segment, productName);
        if (powers.Count == 0 && !string.IsNullOrWhiteSpace(currentPower))
        {
            powers = new List<string> { currentPower };
        }
        var quantity = ExtractQuantity(segment);
        var isTrial = segment.Contains("试戴", StringComparison.OrdinalIgnoreCase) ||
                      segment.Contains("试用", StringComparison.OrdinalIgnoreCase);
        var isOutOfStock = segment.Contains("缺货", StringComparison.OrdinalIgnoreCase);
        var remark = ExtractItemRemark(segment, isOutOfStock);

        item = new OrderItem
        {
            RawText = segment,
            ProductName = productName,
            Quantity = quantity,
            IsTrial = isTrial,
            IsOutOfStock = isOutOfStock,
            LocalWearPeriodHint = currentWearPeriod,
            Remark = remark,
            MatchSource = "LooseFallback",
            MatchNote = "未命中目录，已按原文保留"
        };

        if (powers.Count >= 2)
        {
            if (string.Equals(powers[0], powers[1], StringComparison.OrdinalIgnoreCase))
            {
                item.LeftPower = powers[0];
                item.PowerSummary = powers[0];
                item.Quantity ??= 1;
            }
            else
            {
                item.LeftPower = powers[0];
                item.RightPower = powers[1];
                item.PowerSummary = $"{powers[0]}/{powers[1]}";
            }
        }
        else if (powers.Count == 1)
        {
            item.LeftPower = powers[0];
            item.PowerSummary = powers[0];
        }

        return true;
    }

    private static bool LooksLikeAddressLikeLooseProductName(string value)
    {
        var cleaned = CleanupFreeText(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        return Regex.IsMatch(
            cleaned,
            @"(?:省|市|区|县|镇|乡|街道|大道|路|号|楼|单元|室|园|仓|驿站|校区|大厦|公寓|科技园|菜鸟|库|@|#)",
            RegexOptions.IgnoreCase);
    }

    private static IEnumerable<OrderItem> BuildTrailingPowerQuantityItems(
        string text,
        string productName,
        string? currentPower,
        string? currentWearPeriod,
        bool isTrial,
        bool isOutOfStock,
        string? remark)
    {
        if (!Regex.IsMatch(text, @"(?:左眼|右眼)", RegexOptions.IgnoreCase))
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(text, @"(?<![\d.])(?<power>\d{1,4})\s*[xX×*＊]\s*(?<quantity>\d+)(?!\s*片)", RegexOptions.IgnoreCase))
        {
            var power = match.Groups["power"].Value;
            if (string.IsNullOrWhiteSpace(power))
            {
                continue;
            }

            var quantity = int.TryParse(match.Groups["quantity"].Value, out var parsedQuantity)
                ? parsedQuantity
                : 1;

            yield return new OrderItem
            {
                RawText = CleanupFreeText($"{productName}{power}*{quantity}"),
                ProductName = productName,
                Quantity = quantity,
                IsTrial = isTrial,
                IsOutOfStock = isOutOfStock,
                LocalWearPeriodHint = currentWearPeriod,
                Remark = remark,
                LeftPower = power,
                PowerSummary = power
            };
        }
    }

    private static void ApplyImplicitZeroPowerContext(ParsedOrder order)
    {
        var explicitPowers = order.Items
            .SelectMany(item => ExtractPowers(item.RawText, item.ProductName))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasExplicitNonZeroPower = explicitPowers.Any(value => !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase));
        var shouldDefaultAllMissingToZero = explicitPowers.Count == 0 || explicitPowers.All(value => string.Equals(value, "0", StringComparison.OrdinalIgnoreCase));

        foreach (var item in order.Items)
        {
            if (!string.IsNullOrWhiteSpace(item.PowerSummary) ||
                !string.IsNullOrWhiteSpace(item.LeftPower) ||
                !string.IsNullOrWhiteSpace(item.RightPower))
            {
                continue;
            }

            if (ContainsExplicitZeroPowerCue(item.RawText))
            {
                item.LeftPower = "0";
                item.PowerSummary = "0";
                continue;
            }

            if (hasExplicitNonZeroPower)
            {
                continue;
            }

            if (shouldDefaultAllMissingToZero)
            {
                item.LeftPower = "0";
                item.PowerSummary = "0";
            }
        }
    }

    private static bool ContainsExplicitZeroPowerCue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Regex.IsMatch(
            text,
            @"(?:^|[^\d])(0)(?:\s*(?:度|度数)|\s*/\s*0|\s*(?:无度数|無度數|无度|無度|平光))|(?:无度数|無度數|无度|無度|平光)",
            RegexOptions.IgnoreCase);
    }

    private static string? ExtractLooseProductName(string segment)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        cleaned = Regex.Replace(
            cleaned,
            @"\s*[（(]\s*共[^）)]*(?:款|副|盒|个|片|系列)[^）)]*[）)]\s*$",
            string.Empty,
            RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(
            cleaned,
            @"\s+[\p{IsCJKUnifiedIdeographs}A-Za-z]{1,4}[/／](?=(?:北京|上海|天津|重庆|[\p{IsCJKUnifiedIdeographs}]{2,}(?:省|市|自治区|特别行政区))).*$",
            string.Empty,
            RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"^(?:款式|下单|商品|备注|品牌)\s*[:：]?\s*", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(
            cleaned,
            @"(?:[，,、;；\s]*(?:[xX×*＊]\s*\d+|\d+\s*(?:副|幅|盒|个|片)|[一二两三四五六七八九十]+\s*(?:副|幅|盒|个|片)))+\s*$",
            string.Empty,
            RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(
            cleaned,
            @"(?:\d+|[一二两三四五六七八九十]+)\s*(?:副|幅|付|盒|个|片)(?=\s*\d{1,4}(?:度|度数)?\s*$)",
            string.Empty,
            RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(
            cleaned,
            @"(?:[，,、;；\s]*\d{1,4}\s*(?:度|度数)?|\s*\d{1,4})\s*$",
            string.Empty,
            RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[\s,;，；]+$", string.Empty);
        cleaned = Regex.Replace(cleaned, @"^[\s,;，；]+", string.Empty);

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        if (LooksLikeOrderMetaToken(cleaned) || PhoneRegex.IsMatch(cleaned) || LandlineRegex.IsMatch(cleaned))
        {
            return null;
        }

        return cleaned;
    }

    private static string BuildMatchReadyItemText(string text)
    {
        var cleaned = CleanupFreeText(text);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        cleaned = Regex.Replace(cleaned, @"^(?:款式|下单|商品|备注|品牌)\s*[:：]\s*", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"(?:^|[\s,;])(?:日抛\d*片装?|日抛|年抛|半年抛|月抛|季抛|试戴片?|试用)(?=$|[\s,;])", " ", RegexOptions.IgnoreCase);
        cleaned = StripNoiseKeywords(cleaned);
        cleaned = Regex.Replace(cleaned, @"[+]+", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return cleaned.Trim(' ', ',', ';');
    }

    private static string StripNoiseKeywords(string text)
    {
        var cleaned = text;
        foreach (var keyword in ItemNoiseKeywords)
        {
            cleaned = Regex.Replace(
                cleaned,
                $@"(?:^|[\s,;，；]){Regex.Escape(keyword)}(?=$|[\s,;，；:：])",
                " ",
                RegexOptions.IgnoreCase);
        }

        return cleaned;
    }

    private static bool TrySplitOralPowerItems(
        string normalized,
        string productName,
        bool isTrial,
        bool isOutOfStock,
        string? remark,
        string? currentWearPeriod,
        out List<OrderItem> items)
    {
        items = new List<OrderItem>();
        var matches = Regex.Matches(normalized, @"(?:一个|1个|各一个|各1个|一副一个)?\s*(\d{1,4})\s*度", RegexOptions.IgnoreCase);
        if (matches.Count < 2)
        {
            return false;
        }

        var distinctPowers = matches
            .Select(match => match.Groups[1].Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (distinctPowers.Count < 2)
        {
            return false;
        }

        foreach (var power in distinctPowers)
        {
            items.Add(new OrderItem
            {
                RawText = normalized,
                ProductName = productName,
                Quantity = 1,
                IsTrial = isTrial,
                IsOutOfStock = isOutOfStock,
                LocalWearPeriodHint = currentWearPeriod,
                Remark = remark,
                LeftPower = power,
                PowerSummary = power
            });
        }

        return true;
    }

    private static string? ExtractItemRemark(string normalized, bool isOutOfStock)
    {
        var parts = new List<string>();
        if (isOutOfStock)
        {
            parts.Add("缺货");
        }

        if (normalized.Contains("售后", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("售后");
        }

        return parts.Count == 0 ? null : string.Join('；', parts);
    }

    private static string? FindProductName(string text, ParseIndex parseIndex)
    {
        var compactText = CompactForMatch(text);
        ProductAliasToken? bestMatch = null;
        var bestLength = -1;

        foreach (var alias in parseIndex.ProductAliases)
        {
            if (!AliasMatchesText(text, compactText, alias.Alias, alias.CompactAlias))
            {
                continue;
            }

            var aliasLength = alias.CompactAlias.Length;
            if (aliasLength <= bestLength)
            {
                continue;
            }

            bestMatch = alias;
            bestLength = aliasLength;
        }

        return bestMatch?.CanonicalName;
    }

    private static bool AliasMatchesText(string rawText, string compactText, string alias, string compactAlias)
    {
        if (string.IsNullOrWhiteSpace(compactAlias))
        {
            return false;
        }

        if (LooksLikeNumericPowerAlias(alias))
        {
            var pattern = $@"(?<!\d){Regex.Escape(alias).Replace("/", @"\s*[/\-]\s*")}(?!\d)";
            return Regex.IsMatch(rawText, pattern, RegexOptions.IgnoreCase);
        }

        return compactText.Contains(compactAlias, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeNumericPowerAlias(string alias)
    {
        return Regex.IsMatch(alias.Trim(), @"^\d{1,4}\s*[/\-]\s*\d{1,4}$");
    }

    private static bool LooksLikeNonProductDetail(string segment, string productName)
    {
        var normalizedProductName = CleanupFreeText(productName);
        if (!Regex.IsMatch(normalizedProductName, @"^\d{1,4}(?:\s*[/\-]\s*\d{1,4})?$"))
        {
            return false;
        }

        var stripped = CleanupFreeText(segment);
        stripped = Regex.Replace(stripped, @"(?:共)?\s*\d+\s*(?:副|幅|盒|个|片)", " ", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"[一二两三四五六七八九十]+\s*(?:副|幅|盒|个|片)", " ", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"[xX×*＊]\s*\d+", " ", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"(?:试戴|试用|缺货)", " ", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"\s+", string.Empty);

        return Regex.IsMatch(stripped, @"^\d{1,4}(?:[/\-]\d{1,4})?$");
    }

    /// <summary>
    /// Removes trailing "收件人/地址" tails from mixed lines so the product portion can still
    /// be parsed when marketplaces concatenate item, receiver and address into one segment.
    /// </summary>
    private static string TrimTrailingContactTail(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return string.Empty;
        }

        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        var match = Regex.Match(
            cleaned,
            @"^(?<item>.+?)\s+[\p{IsCJKUnifiedIdeographs}A-Za-z]{1,8}\s*(?:[\/／\\-])\s*(?:(?:北京|上海|天津|重庆|[\p{IsCJKUnifiedIdeographs}]{2,}(?:省|市|自治区|特别行政区)).*)$",
            RegexOptions.IgnoreCase);

        return match.Success ? CleanupFreeText(match.Groups["item"].Value) : cleaned;
    }

    private static bool LooksLikeItemDetailFragment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        if (PhoneRegex.IsMatch(cleaned) || LandlineRegex.IsMatch(cleaned))
        {
            return false;
        }

        if (Regex.IsMatch(cleaned, @"(?:北京|上海|天津|重庆|[\p{IsCJKUnifiedIdeographs}]{2,}(?:省|市|自治区|特别行政区)).*(?:区|县|街道|路|号|楼|室|园|仓|驿站|校区)", RegexOptions.IgnoreCase))
        {
            return false;
        }

        if (TryDetectStandalonePowerHeading(cleaned, out _))
        {
            return false;
        }

        return Regex.IsMatch(cleaned, @"^(?:试戴片?|试用|官网直发|现货|日抛|年抛|半年抛|月抛|季抛|护理液|护理盒|盒子|镜盒|\[.*?\]|\d{1,4}(?:\s*[/\-]\s*\d{1,4})?(?:度)?(?:\s*(?:一|二|两|三|四|五|六|七|八|九|十|\d+)\s*(?:副|盒|个|片))?|[xX×*＊]\s*\d+|共\s*(?:一|二|两|三|四|五|六|七|八|九|十|\d+)\s*(?:副|盒|个|片))$", RegexOptions.IgnoreCase);
    }

    private static List<string> ExtractPowers(string text, string? productName = null)
    {
        var preferredText = BuildPreferredPowerText(text, productName);
        var slashMatch = Regex.Match(text, @"(?<![\d.])(\d{1,4})\s*/\s*(\d{1,4})(?!\d)");
        if (slashMatch.Success)
        {
            return new List<string> { slashMatch.Groups[1].Value, slashMatch.Groups[2].Value };
        }

        slashMatch = Regex.Match(preferredText, @"(?<![\d.])(\d{1,4})\s*/\s*(\d{1,4})(?!\d)");
        if (slashMatch.Success)
        {
            return new List<string> { slashMatch.Groups[1].Value, slashMatch.Groups[2].Value };
        }

        var labeledPower = Regex.Match(preferredText, @"(?:度数|度)\s*[:：]?\s*(\d{1,4})(?!\d)", RegexOptions.IgnoreCase);
        if (labeledPower.Success)
        {
            return new List<string> { labeledPower.Groups[1].Value };
        }

        var leftEyePower = Regex.Match(preferredText, @"左眼\s*[:：]?\s*(\d{1,4})(?!\d)", RegexOptions.IgnoreCase);
        if (leftEyePower.Success)
        {
            return new List<string> { leftEyePower.Groups[1].Value };
        }

        var degreeMatches = Regex.Matches(preferredText, @"(\d{1,4})\s*度");
        if (degreeMatches.Count > 0)
        {
            return degreeMatches.Select(item => item.Groups[1].Value).ToList();
        }

        if (Regex.IsMatch(preferredText, @"(?:无度数|無度數|无度|無度|平光)", RegexOptions.IgnoreCase))
        {
            return new List<string> { "0" };
        }

        // Treat "10片/2片" as packaging metadata instead of a power cue so titles like
        // "笼中梦红（日抛）*10片" can still fall back to implicit 0度 when no degree is written.
        var powerBeforeQuantity = Regex.Match(preferredText, @"(?<![\d.])(\d{1,4})(?=\s*(?:\d+|[一二两三四五六七八九十])\s*(?:副|幅|付|盒|个))");
        if (powerBeforeQuantity.Success)
        {
            return new List<string> { powerBeforeQuantity.Groups[1].Value };
        }

        var candidate = Regex.Replace(preferredText, @"\d+(?:\.\d+)?\s*mm", " ", RegexOptions.IgnoreCase);
        candidate = Regex.Replace(candidate, @"(?:共)?\s*(?:\d+|[一二两三四五六七八九十])\s*(?:副|幅|付|盒|个|片)", " ");
        candidate = Regex.Replace(candidate, @"[xX×*＊]\s*\d+", " ");
        candidate = Regex.Replace(candidate, @"(?:日抛|日拋)\s*\d+", " ", RegexOptions.IgnoreCase);
        candidate = Regex.Replace(candidate, @"缺货", " ", RegexOptions.IgnoreCase);

        var trailingMatches = Regex.Matches(candidate, @"(?<![\d.])(\d{1,4})(?!\d)");
        if (trailingMatches.Count > 0)
        {
            return new List<string> { trailingMatches[^1].Groups[1].Value };
        }

        return new List<string>();
    }

    private static void ApplyOrderWidePowerContext(ParsedOrder order, string orderWidePower)
    {
        if (string.IsNullOrWhiteSpace(orderWidePower))
        {
            return;
        }

        foreach (var item in order.Items)
        {
            if (!string.IsNullOrWhiteSpace(item.PowerSummary) ||
                !string.IsNullOrWhiteSpace(item.LeftPower) ||
                !string.IsNullOrWhiteSpace(item.RightPower))
            {
                continue;
            }

            item.LeftPower = orderWidePower;
            item.PowerSummary = orderWidePower;
        }
    }

    /// <summary>
    /// Carries an explicitly written power forward within the same source line so compact
    /// chains like "塞壬泉紫375度 星辰泪金棕 玛瑙冰蓝" can reuse the 375 until a new power appears.
    /// </summary>
    private static bool TryDetectInlinePowerContext(string segment, IReadOnlyList<OrderItem> parsedItems, out string power)
    {
        power = string.Empty;

        if (string.IsNullOrWhiteSpace(segment) || parsedItems.Count == 0)
        {
            return false;
        }

        var productName = parsedItems[0].ProductName;
        var powers = ExtractPowers(segment, productName);
        if (powers.Count != 1)
        {
            return false;
        }

        power = powers[0];
        return !string.IsNullOrWhiteSpace(power);
    }

    private static string DetectOrderWidePower(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Regex.Match(
                line,
                @"(?:以上|上述|下述|前面|前述|全(?:部|为|都)|均为|统一|都为|都按)[^0-9\r\n]{0,12}(?<degree>\d{1,4})\s*(?:度|度数)",
                RegexOptions.IgnoreCase);

            if (match.Success)
            {
                return match.Groups["degree"].Value;
            }
        }

        return string.Empty;
    }

    private static string BuildPreferredPowerText(string text, string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return text;
        }

        var index = text.IndexOf(productName, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return text;
        }

        var tail = text[(index + productName.Length)..].Trim();
        return string.IsNullOrWhiteSpace(tail) ? text : tail;
    }

    private static int? ExtractQuantity(string text)
    {
        var normalized = Regex.Replace(text, @"共\s*(\d+|[一二两三四五六七八九十]+)\s*(?:副|幅|付|盒|个|片)", " ", RegexOptions.IgnoreCase);

        // "10片/2片" is usually packaging metadata, not ordered count.
        var match = Regex.Match(normalized, @"(\d+)\s*(?:副|幅|付|盒|个)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var quantity))
        {
            return quantity;
        }

        match = Regex.Match(normalized, @"[xX×*＊]\s*(\d+)(?!\s*片)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out quantity))
        {
            return quantity;
        }

        var chineseQuantity = Regex.Match(normalized, @"([一二两三四五六七八九十])\s*(?:副|幅|付|盒|个)");
        if (chineseQuantity.Success)
        {
            return ChineseNumberToInt(chineseQuantity.Groups[1].Value);
        }

        if (normalized.Contains("一副", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("一幅", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("一付", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("一盒", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return null;
    }

    private static bool LooksLikeStrongFallbackName(string value, ParserRuleSet ruleSet)
    {
        value = SanitizeName(value);
        if (!IsPossibleName(value, ruleSet))
        {
            return false;
        }

        if (value.Length > 4)
        {
            return false;
        }

        if (Regex.IsMatch(value, @"[\u4e00-\u9fa5]") && Regex.IsMatch(value, @"^[\p{IsCJKUnifiedIdeographs}]{1,4}$"))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeContactLabelToken(string value)
    {
        var cleaned = SanitizeName(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return true;
        }

        return Regex.IsMatch(
            cleaned,
            @"(?:手机号码|手机号|电话|联系号码|联系方式|联系人|收件人|收货人|姓名|名字|客户|地址|所在地区|详细地址)",
            RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeReceiverNameWithNumericSuffix(string value)
    {
        var cleaned = SanitizeName(value);
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length > 18)
        {
            return false;
        }

        return Regex.IsMatch(
            cleaned,
            @"^[\p{IsCJKUnifiedIdeographs}A-Za-z]{1,8}\d{1,2}(?:\.\d{1,2})?(?:號|号)?$",
            RegexOptions.IgnoreCase);
    }

    private static bool ContainsItemLikeClues(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Regex.IsMatch(
            text,
            @"(?:\d{1,4}\s*度|[xX×*＊]\s*\d+|日抛|年抛|半年抛|月抛|季抛|试戴|试用|片装|副|幅|付|盒|个|片)",
            RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeOrderMetaToken(string value)
    {
        var cleaned = SanitizeName(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return true;
        }

        return Regex.IsMatch(
            cleaned,
            @"(?:下单|订单|订单号|订单编号|商品信息|商品|款式|品牌|备注|手机号码|手机号|电话|電話|收件人|收货人|收貨人|姓名|名字|客户|邮政编码|郵政編[號号]|邮编|数量|收貨地址|所在地區|詳細地址|聯系電話|联系电话)",
            RegexOptions.IgnoreCase);
    }

    private void ApplyContactLineContext(List<string> lines, ParserRuleSet ruleSet, ParsedOrder order)
    {
        foreach (var line in lines)
        {
            if (!TryExtractContactLineContext(line, ruleSet, out var nameFromContact, out var addressFromContact))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(order.CustomerName) && !string.IsNullOrWhiteSpace(nameFromContact))
            {
                order.CustomerName = nameFromContact;
            }

            if (string.IsNullOrWhiteSpace(order.Address) && !string.IsNullOrWhiteSpace(addressFromContact))
            {
                order.Address = addressFromContact;
            }

            if (!string.IsNullOrWhiteSpace(order.CustomerName) && !string.IsNullOrWhiteSpace(order.Address))
            {
                return;
            }
        }
    }

    private static bool TryExtractContactLineContext(string line, ParserRuleSet ruleSet, out string? name, out string? address)
    {
        name = null;
        address = null;

        var cleanedLine = CleanupFreeText(line);
        if (string.IsNullOrWhiteSpace(cleanedLine))
        {
            return false;
        }

        var phoneMatch = PhoneRegex.Match(cleanedLine);
        if (!phoneMatch.Success)
        {
            return false;
        }

        if (phoneMatch.Index > 0)
        {
            var beforePhone = cleanedLine[..phoneMatch.Index];
            name = beforePhone.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(SanitizeName)
                .LastOrDefault(candidate => (IsPossibleName(candidate, ruleSet) || LooksLikeReceiverNameWithNumericSuffix(candidate)) && !LooksLikeContactLabelToken(candidate) && !LooksLikeOrderMetaToken(candidate));
        }

        var afterPhone = cleanedLine[(phoneMatch.Index + phoneMatch.Length)..]
            .Trim(' ', ',', '，', ';', '；', ':', '：', '-', '_');

        if (string.IsNullOrWhiteSpace(afterPhone))
        {
            return !string.IsNullOrWhiteSpace(name);
        }

        if (LooksLikeAddressAfterPhone(afterPhone, ruleSet))
        {
            address = CleanupAddress(afterPhone);
        }

        return !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(address);
    }

    private static bool LooksLikeAddressAfterPhone(string text, ParserRuleSet ruleSet)
    {
        var cleaned = CleanupFreeText(text);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        var addressHits = ruleSet.AddressKeywords.Count(keyword => cleaned.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (addressHits >= 2)
        {
            return true;
        }

        if (addressHits >= 1 && Regex.IsMatch(cleaned, @"\d"))
        {
            return true;
        }

        return Regex.IsMatch(cleaned, @"(?:北京|上海|天津|重庆|.+省|.+自治区|.+特别行政区).+(?:市|州|盟|地区).+(?:区|县|旗|镇|乡|街道|路|号)", RegexOptions.IgnoreCase);
    }

    private static string? TryExtractNameFromAddressLikeLine(string line, ParserRuleSet ruleSet)
    {
        var cleanedLine = CleanupFreeText(line);
        if (string.IsNullOrWhiteSpace(cleanedLine) || !LooksLikeAddressLikeLine(cleanedLine, ruleSet))
        {
            return null;
        }

        var match = Regex.Match(
            cleanedLine,
            @"(?<name>[\p{IsCJKUnifiedIdeographs}A-Za-z]{1,8})\s*(?:[\/／\\\-]\s*|\s+)(?=(?:北京|上海|天津|重庆|[\p{IsCJKUnifiedIdeographs}]{2,}(?:省|自治区|特别行政区)|[\p{IsCJKUnifiedIdeographs}]{2,}(?:市|州|盟|地区|区|县|镇|乡|街道|大道|路|号)))",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return null;
        }

        var candidate = SanitizeName(match.Groups["name"].Value);
        return IsPossibleName(candidate, ruleSet) ? candidate : null;
    }

    private static bool IsRegionAddressLabel(string label)
    {
        return label.Contains("所在地区", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDetailAddressLabel(string label)
    {
        return label.Contains("详细地址", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("地址", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("收货地址", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("配送地址", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ComposeExplicitAddress(string? region, string? detail)
    {
        region = CleanupAddress(region ?? string.Empty);
        detail = CleanupAddress(detail ?? string.Empty);

        if (string.IsNullOrWhiteSpace(region))
        {
            return string.IsNullOrWhiteSpace(detail) ? null : detail;
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            return region;
        }

        if (detail.Contains(region, StringComparison.OrdinalIgnoreCase))
        {
            return detail;
        }

        var compactRegion = MatchTextHelper.Compact(region);
        var compactDetail = MatchTextHelper.Compact(detail);
        if (!string.IsNullOrWhiteSpace(compactRegion) &&
            !string.IsNullOrWhiteSpace(compactDetail) &&
            compactDetail.Contains(compactRegion, StringComparison.OrdinalIgnoreCase))
        {
            return detail;
        }

        return $"{region}{detail}";
    }

    private static int ChineseNumberToInt(string value)
    {
        return value switch
        {
            "一" => 1,
            "二" => 2,
            "两" => 2,
            "三" => 3,
            "四" => 4,
            "五" => 5,
            "六" => 6,
            "七" => 7,
            "八" => 8,
            "九" => 9,
            "十" => 10,
            _ => 1
        };
    }

    private static bool LooksLikeMetadata(string segment, ParserRuleSet ruleSet, ParseIndex parseIndex, ParsedOrder order)
    {
        var containsProductAlias = ContainsKnownProductAlias(segment, parseIndex);

        if (IsIgnorableOrderMetadataLine(segment, parseIndex))
        {
            return true;
        }

        if (LooksLikeItemDetailFragment(segment))
        {
            return false;
        }

        if (TryDetectOrderWidePower(segment, out _))
        {
            return true;
        }

        if (ContainsItemLikeClues(segment))
        {
            return false;
        }

        if (!containsProductAlias && LooksLikeAddressLikeLine(segment, ruleSet))
        {
            return true;
        }

        if (PhoneRegex.IsMatch(segment) && !containsProductAlias)
        {
            return true;
        }

        if (LandlineRegex.IsMatch(segment) && !containsProductAlias)
        {
            return true;
        }

        if (!containsProductAlias && !string.IsNullOrWhiteSpace(order.Address) && CleanupFreeText(segment).Contains(order.Address, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!containsProductAlias && !string.IsNullOrWhiteSpace(order.Brand) && segment.Contains(order.Brand, StringComparison.OrdinalIgnoreCase) && segment.Length <= 20)
        {
            return true;
        }

        if (!containsProductAlias && !string.IsNullOrWhiteSpace(order.WearPeriod) && segment.Contains(order.WearPeriod, StringComparison.OrdinalIgnoreCase) && segment.Length <= 16)
        {
            return true;
        }

        return !containsProductAlias &&
               ruleSet.NoiseKeywords.Any(keyword => segment.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsIgnorableOrderMetadataLine(string segment, ParseIndex parseIndex)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        if (Regex.IsMatch(cleaned, @"^(?:下单|下單)$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(cleaned, @"(?:https?://|www\.)", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(cleaned, @"^(?:订单号|订单编号|订单链接|订单地址|订单详情|订单信息|链接|网址|url)\s*[:：#-]?\s*[\w\-:/?=&.%]+$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(cleaned, @"^(?:邮政编码|郵政編[號号]|邮编)\s*[:：]?\s*\d{4,10}$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(cleaned, @"^(?:数量)\s*[:：]?\s*(?:\d+|[一二两三四五六七八九十]+)\s*(?:副|幅|付|盒|个|片)$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (!Regex.IsMatch(cleaned, @"^(?:商品信息|商品)\s*[:：]", RegexOptions.IgnoreCase))
        {
            return false;
        }

        var value = Regex.Replace(cleaned, @"^(?:商品信息|商品)\s*[:：]\s*", string.Empty, RegexOptions.IgnoreCase);
        if (ContainsKnownProductAlias(value, parseIndex))
        {
            return false;
        }

        return LooksLikePromotionalProductInfo(value);
    }

    private static bool LooksLikePromotionalProductInfo(string text)
    {
        var cleaned = CleanupFreeText(text);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return true;
        }

        if (Regex.IsMatch(cleaned, @"(?<![\d.])\d{1,4}\s*(?:度|度数)", RegexOptions.IgnoreCase))
        {
            return false;
        }

        return Regex.IsMatch(
            cleaned,
            @"(?:活动|促销|上市|新品|限时|包邮|赠品|福利|清仓|专区|十盒|十副|513区|新品竖瞳|瞳物语|星品)",
            RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeAddressLikeLine(string segment, ParserRuleSet ruleSet)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        if (ruleSet.AddressLabels.Any(label => cleaned.Contains(label, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var addressHits = ruleSet.AddressKeywords.Count(keyword => cleaned.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (addressHits >= 3)
        {
            return true;
        }

        if (addressHits >= 2 && Regex.IsMatch(cleaned, @"\d"))
        {
            return true;
        }

        if (Regex.IsMatch(cleaned, @"(?:北京|上海|天津|重庆|[\p{IsCJKUnifiedIdeographs}]{2,}(?:省|市|自治区|特别行政区)).*(?:区|县|镇|乡|街道|大道|路|号|楼|单元|室|园|村|仓|驿站|校区|大厦|家园)", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if ((PhoneRegex.IsMatch(cleaned) || LandlineRegex.IsMatch(cleaned)) && addressHits >= 1)
        {
            return true;
        }

        return false;
    }

    private static bool TryDetectOrderWidePower(string text, out string degree)
    {
        degree = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = Regex.Match(
            text,
            @"(?:以上|上述|下述|前面|前述|全(?:部|为|都)|均为|统一|都为|都按)[^0-9\r\n]{0,12}(?<degree>\d{1,4})\s*(?:度|度数)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        degree = match.Groups["degree"].Value;
        return true;
    }

    private static bool TryDetectStandalonePowerHeading(string text, out string degree)
    {
        degree = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = CleanupFreeText(text);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        var match = Regex.Match(cleaned, @"^(?<degree>\d{1,4})\s*(?:度|度数)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        degree = match.Groups["degree"].Value;
        return true;
    }

    private static bool TryDetectLineTrailingPower(string text, ParseIndex parseIndex, out string degree)
    {
        degree = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = CleanupFreeText(text);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        if (!ContainsKnownProductAlias(cleaned, parseIndex) && !LooksLikeProductCandidate(cleaned))
        {
            return false;
        }

        var match = Regex.Match(cleaned, @"(?<degree>\d{1,4})\s*(?:度|度数)\s*$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        degree = match.Groups["degree"].Value;
        return true;
    }

    private static bool TrySplitVariantTail(string token, out string variant, out string tail)
    {
        variant = CleanupFreeText(token);
        tail = string.Empty;

        if (string.IsNullOrWhiteSpace(variant))
        {
            return false;
        }

        var splitIndex = FindVariantTailStart(variant);
        if (splitIndex <= 0)
        {
            return true;
        }

        var candidateVariant = variant[..splitIndex].Trim();
        var candidateTail = variant[splitIndex..].Trim();
        if (string.IsNullOrWhiteSpace(candidateVariant))
        {
            return false;
        }

        variant = candidateVariant;
        tail = candidateTail;
        return true;
    }

    private static int FindVariantTailStart(string token)
    {
        var chainedTailIndex = token.IndexOf("各来", StringComparison.Ordinal);
        if (chainedTailIndex >= 0)
        {
            return chainedTailIndex;
        }

        var genericTailIndex = token.IndexOf("各", StringComparison.Ordinal);
        if (genericTailIndex > 0)
        {
            return genericTailIndex;
        }

        var digitMatch = Regex.Match(token, @"\d");
        if (digitMatch.Success)
        {
            return digitMatch.Index;
        }

        return -1;
    }

    private static bool LooksLikeGenericCountToken(string token)
    {
        var cleaned = CleanupFreeText(token);
        return Regex.IsMatch(cleaned, @"^(?:一|一个|1|1个|一副|1副|一盒|1盒|一片|1片|各一|各1|各1个|各1副)$", RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeGiftOrAccessoryLine(string segment, ParserRuleSet ruleSet, ParseIndex parseIndex)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        var containsProductAlias = ContainsKnownProductAlias(cleaned, parseIndex);
        if (containsProductAlias)
        {
            return false;
        }

        if (AccessoryKeywords.Any(keyword => cleaned.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if ((cleaned.Contains("赠", StringComparison.OrdinalIgnoreCase) ||
             cleaned.Contains("送", StringComparison.OrdinalIgnoreCase)) &&
            Regex.IsMatch(cleaned, @"(?:\d+|[一二两三四五六七八九十]+)?\s*(?:个|副|盒|瓶|支|套|片)?$|(?:护理|盒|液|工具)", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeProductCandidate(string segment)
    {
        if (segment.Length <= 1)
        {
            return false;
        }

        if (PhoneRegex.IsMatch(segment))
        {
            return false;
        }

        if (LooksLikeStandaloneModelName(segment))
        {
            return true;
        }

        return Regex.IsMatch(segment, @"[\u4e00-\u9fa5A-Za-z]{2,}") &&
               (segment.Contains("度", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("/", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("片", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("盒", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("x", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(segment, @"\d{1,4}"));
    }

    private static bool LooksLikeStandaloneModelName(string segment)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length > 24)
        {
            return false;
        }

        if (LooksLikeOrderMetaToken(cleaned) ||
            LooksLikeGenericWearHeader(cleaned) ||
            PhoneRegex.IsMatch(cleaned) ||
            LandlineRegex.IsMatch(cleaned))
        {
            return false;
        }

        if (Regex.IsMatch(cleaned, @"(?:省|市|区|县|镇|乡|街道|大道|路|号|楼|单元|室|园|仓|驿站|校区|公寓|大厦)", RegexOptions.IgnoreCase))
        {
            return false;
        }

        if (!Regex.IsMatch(cleaned, @"[\p{IsCJKUnifiedIdeographs}A-Za-z]{2,}"))
        {
            return false;
        }

        var normalized = ExtractLooseProductName(cleaned);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Contains("pro", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return VariantSuffixes.Any(suffix =>
            normalized.Length > suffix.Length &&
            normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindCanonicalAlias(string text, IReadOnlyList<CanonicalAliasToken> aliases)
    {
        var compactText = CompactForMatch(text);
        string? bestMatch = null;
        var bestLength = -1;

        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias.CompactAlias) ||
                !compactText.Contains(alias.CompactAlias, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (alias.CompactAlias.Length <= bestLength)
            {
                continue;
            }

            bestMatch = alias.CanonicalName;
            bestLength = alias.CompactAlias.Length;
        }

        return bestMatch;
    }

    private static string? ExtractPhone(string text)
    {
        var matches = PhoneRegex.Matches(text);
        if (matches.Count > 0)
        {
            var best = matches
                .Select(match => new
                {
                    LocalPhone = NormalizePhoneValue(match.Groups[1].Value),
                    Extension = match.Groups[2].Success ? match.Groups[2].Value : null,
                    Score = match.Value.Contains("转", StringComparison.OrdinalIgnoreCase) || match.Groups[2].Success ? 2 : 0
                })
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.LocalPhone.Length)
                .First();

            return string.IsNullOrWhiteSpace(best.Extension) ? best.LocalPhone : $"{best.LocalPhone}转{best.Extension}";
        }

        var landlineMatch = LandlineRegex.Match(text);
        if (landlineMatch.Success)
        {
            return landlineMatch.Groups[1].Value;
        }

        return null;
    }

    private static bool IsPossibleName(string value, ParserRuleSet ruleSet)
    {
        value = SanitizeName(value);
        if (string.IsNullOrWhiteSpace(value) || value.Length > 18)
        {
            return false;
        }

        if (Regex.IsMatch(value, @"^\d+$"))
        {
            return false;
        }

        if (Regex.IsMatch(value, @"^\d+[副盒个片]?$"))
        {
            return false;
        }

        if (PhoneRegex.IsMatch(value))
        {
            return false;
        }

        if (Regex.IsMatch(value, @"\d") ||
            Regex.IsMatch(value, @"(?:度|[/\\/]|[xX×*＊])") ||
            Regex.IsMatch(value, @"(?:pro|日抛|半年抛|年抛|月抛|季抛|试戴|试用|lenspop|leea)", RegexOptions.IgnoreCase))
        {
            return false;
        }

        var addressHits = ruleSet.AddressKeywords.Count(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (addressHits >= 2 || Regex.IsMatch(value, @"(?:省|市|区|县|街道|大道|路|楼|单元|室|仓|驿站|校区|大厦|家园)$"))
        {
            return false;
        }

        if (ruleSet.ProductAliases.Any(rule => rule.Aliases.Any(alias => CompactForMatch(value).Contains(CompactForMatch(alias), StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        return Regex.IsMatch(value, @"^[\p{IsCJKUnifiedIdeographs}A-Za-z0-9_\-\[\]()*.]+$");
    }

    private static string NormalizePhoneValue(string value)
    {
        var digits = Regex.Replace(value, @"[^\d]", string.Empty);
        if (digits.StartsWith("86", StringComparison.Ordinal) && digits.Length > 11)
        {
            digits = digits[2..];
        }

        return digits.Length > 11 ? digits[^11..] : digits;
    }

    private static string CompactForMatch(string text)
    {
        return MatchTextHelper.Compact(text);
    }

    private static bool ContainsKnownProductAlias(string segment, ParseIndex parseIndex)
    {
        var compactSegment = CompactForMatch(segment);
        foreach (var alias in parseIndex.ProductAliases)
        {
            if (AliasMatchesText(segment, compactSegment, alias.Alias, alias.CompactAlias))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> MergeContinuationSegments(
        IEnumerable<string> segments,
        ParseIndex parseIndex)
    {
        var merged = new List<string>();

        foreach (var rawSegment in segments)
        {
            var segment = CleanupFreeText(rawSegment);
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            if (merged.Count > 0 &&
                !ContainsKnownProductAlias(segment, parseIndex) &&
                LooksLikeItemDetailFragment(segment))
            {
                merged[^1] = CleanupFreeText($"{merged[^1]} {segment}");
                continue;
            }

            merged.Add(segment);
        }

        return merged;
    }

    private static IEnumerable<string> ExpandEnumeratedVariantSegments(string segment, ParseIndex parseIndex)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            yield break;
        }

        var parts = Regex.Split(cleaned, @"[、,;；]+")
            .Select(CleanupFreeText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (parts.Count <= 1)
        {
            yield return cleaned;
            yield break;
        }

        var familyPrefix = ResolveVariantFamilyPrefix(parts[0], parseIndex);
        if (string.IsNullOrWhiteSpace(familyPrefix))
        {
            yield return cleaned;
            yield break;
        }

        var expandableTailExists = parts.Skip(1).Any(part => IsLikelyVariantFragment(part, parseIndex));
        if (!expandableTailExists)
        {
            yield return cleaned;
            yield break;
        }

        yield return parts[0];
        foreach (var part in parts.Skip(1))
        {
            if (FindProductName(part, parseIndex) is not null)
            {
                yield return part;
                continue;
            }

            if (IsLikelyVariantFragment(part, parseIndex))
            {
                yield return $"{familyPrefix}{part}";
                continue;
            }

            yield return part;
        }
    }

    private static IEnumerable<string> ExpandSlashEnumeratedVariantSegments(string segment, ParseIndex parseIndex)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned) || !cleaned.Contains('/'))
        {
            yield return cleaned;
            yield break;
        }

        var parts = Regex.Split(cleaned, @"\s*/\s*")
            .Select(CleanupFreeText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (parts.Count <= 1)
        {
            yield return cleaned;
            yield break;
        }

        var familyPrefix = ResolveVariantFamilyPrefix(parts[0], parseIndex);
        if (string.IsNullOrWhiteSpace(familyPrefix))
        {
            yield return cleaned;
            yield break;
        }

        if (!TrySplitVariantTail(parts[^1], out var lastVariant, out var sharedTail) ||
            (!IsLikelyVariantFragment(lastVariant, parseIndex) && FindProductName(lastVariant, parseIndex) is null))
        {
            yield return cleaned;
            yield break;
        }

        if (LooksLikeGenericCountToken(lastVariant) &&
            Regex.IsMatch(sharedTail, @"^\d{1,4}\s*度(?:数)?$", RegexOptions.IgnoreCase))
        {
            yield return cleaned;
            yield break;
        }

        var expandedParts = new List<string>();

        var firstExpandedPart = parts[0];
        if (!string.IsNullOrWhiteSpace(sharedTail) &&
            !firstExpandedPart.EndsWith(sharedTail, StringComparison.OrdinalIgnoreCase))
        {
            firstExpandedPart = $"{firstExpandedPart}{sharedTail}";
        }

        expandedParts.Add(firstExpandedPart);

        foreach (var part in parts.Skip(1))
        {
            if (!TrySplitVariantTail(part, out var variant, out _))
            {
                yield return cleaned;
                yield break;
            }

            if (!IsLikelyVariantFragment(variant, parseIndex) && FindProductName(variant, parseIndex) is null)
            {
                yield return cleaned;
                yield break;
            }

            expandedParts.Add($"{familyPrefix}{variant}{sharedTail}");
        }

        foreach (var expandedPart in expandedParts)
        {
            yield return expandedPart;
        }
    }

    private static ParseIndex BuildParseIndex(ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries)
    {
        return ParseIndex.Create(ruleSet, catalogEntries);
    }

    private static IEnumerable<string> GetKnownCatalogProductNames(IReadOnlyList<ProductCatalogEntry>? catalogEntries)
    {
        if (catalogEntries is null)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalogEntries)
        {
            foreach (var alias in GetCatalogAliases(entry))
            {
                if (string.IsNullOrWhiteSpace(alias) || !seen.Add(alias))
                {
                    continue;
                }

                yield return alias;
            }
        }
    }

    private static IEnumerable<string> GetCatalogAliases(ProductCatalogEntry entry)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in new[]
                 {
                     GetCatalogDisplayName(entry),
                     entry.ModelToken,
                     entry.BaseName,
                     RemoveSpecificationPrefix(entry.BaseName, entry.SpecificationToken),
                     RemoveSpecificationPrefix(entry.ProductName, entry.SpecificationToken),
                     GetLooseCatalogAlias(entry)
                 })
        {
            foreach (var alias in ExpandCatalogAliasCandidates(candidate))
            {
                if (seen.Add(alias))
                {
                    yield return alias;
                }
            }
        }
    }

    private static IEnumerable<string> ExpandCatalogAliasCandidates(string? candidate)
    {
        var cleaned = CleanupFreeText(candidate ?? string.Empty);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            yield break;
        }

        foreach (var alias in new[]
                 {
                     cleaned,
                     MatchTextHelper.RemoveTrailingDegree(cleaned),
                     RemoveProMarker(cleaned),
                     RemoveProMarker(MatchTextHelper.RemoveTrailingDegree(cleaned))
                 }
                 .Select(CleanupFreeText)
                 .Where(value => !string.IsNullOrWhiteSpace(value))
                 .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsUsableProductAlias(alias))
            {
                yield return alias;
            }
        }
    }

    private static bool IsUsableProductAlias(string? alias)
    {
        var cleaned = CleanupFreeText(alias ?? string.Empty);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        if (Regex.IsMatch(cleaned, @"^\d{1,4}(?:\s*[/\-]\s*\d{1,4})?$"))
        {
            return false;
        }

        var normalized = MatchTextHelper.RemoveTrailingDegree(cleaned);
        normalized = RemoveProMarker(normalized);
        normalized = Regex.Replace(
            normalized,
            @"(?:lenspop|leea|清仓|现货|官网直发|日抛\d*片装?|日抛两片装|日抛十片装|日抛|年抛|半年抛|月抛|季抛|试戴片?|试用)",
            " ",
            RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"[\s,;，；:：\-_]+", string.Empty);

        return Regex.Matches(normalized, @"[\p{IsCJKUnifiedIdeographs}A-Za-z]").Count >= 2;
    }

    private static string GetCatalogDisplayName(ProductCatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ModelToken))
        {
            return entry.ModelToken;
        }

        if (!string.IsNullOrWhiteSpace(entry.BaseName))
        {
            return entry.BaseName;
        }

        return entry.ProductName;
    }

    private static string GetLooseCatalogAlias(ProductCatalogEntry entry)
    {
        var source = !string.IsNullOrWhiteSpace(entry.ModelToken)
            ? entry.ModelToken
            : RemoveSpecificationPrefix(entry.BaseName, entry.SpecificationToken);

        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var alias = source.Trim();
        alias = Regex.Replace(alias, "(深蓝|浅蓝|蓝绿|蓝灰|灰粉|灰蓝|粉紫|玫紫|玫红|金棕|茶棕|橘棕|酒红|深灰|浅灰|棕|蓝|灰|粉|黄|绿|青|紫|黑|白|红|银|金|橙)$", string.Empty, RegexOptions.IgnoreCase);
        return alias.Trim();
    }

    private static bool IsLikelyVariantFragment(string token, ParseIndex parseIndex)
    {
        var cleaned = CleanupFreeText(token);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        if (FindProductName(cleaned, parseIndex) is not null)
        {
            return false;
        }

        var withoutQuantity = Regex.Replace(
            cleaned,
            @"(?:[xX×*＊]\s*\d+|\d+\s*(?:副|盒|个|片))(?=$|[^\p{IsCJKUnifiedIdeographs}A-Za-z])",
            string.Empty,
            RegexOptions.IgnoreCase);

        withoutQuantity = Regex.Replace(withoutQuantity, @"[（(].*$", string.Empty);
        withoutQuantity = Regex.Replace(withoutQuantity, @"\s+", string.Empty);

        return Regex.IsMatch(withoutQuantity, @"^[\p{IsCJKUnifiedIdeographs}A-Za-z]{1,6}$");
    }

    private static string GetVariantFamilyPrefix(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return string.Empty;
        }

        var cleaned = CleanupFreeText(productName);
        foreach (var suffix in VariantSuffixes.OrderByDescending(item => item.Length))
        {
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                cleaned.Length > suffix.Length)
            {
                return cleaned[..^suffix.Length].Trim();
            }
        }

        return string.Empty;
    }

    private static string ResolveVariantFamilyPrefix(string segment, ParseIndex parseIndex)
    {
        var rawPrefix = GetVariantFamilyPrefix(ExtractLooseProductName(segment));
        if (!string.IsNullOrWhiteSpace(rawPrefix))
        {
            return rawPrefix;
        }

        return GetVariantFamilyPrefix(FindProductName(segment, parseIndex));
    }

    private static string RemoveProMarker(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text, @"\s*pro\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
    }

    private static string RemoveSpecificationPrefix(string? text, string? specificationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(specificationToken))
        {
            return text.Trim();
        }

        return text.StartsWith(specificationToken, StringComparison.OrdinalIgnoreCase)
            ? text[specificationToken.Length..].Trim()
            : text.Trim();
    }

    private static string CleanupAddress(string text)
    {
        var address = CleanupFreeText(text);
        address = Regex.Replace(address, @"(?:收货人|收件人|姓名|名字)\s*[:：][^,，;；]+[,，;；]?", " ", RegexOptions.IgnoreCase);
        address = Regex.Replace(address, @"(?:手机号码|手机号|联系电话|聯系電話|电话|電話)\s*[:：][^,，;；]+[,，;；]?", " ", RegexOptions.IgnoreCase);
        address = Regex.Replace(address, @"(?:邮政编码|郵政編號|邮编)\s*[:：]?\s*\d{4,10}", " ", RegexOptions.IgnoreCase);
        address = Regex.Replace(address, @"(?:所在地区|详细地址|收货地址|地址)\s*[:：]", " ", RegexOptions.IgnoreCase);
        address = PhoneRegex.Replace(address, " ");
        address = Regex.Replace(address, @"^.*?(?=(?:北京|上海|天津|重庆|[\p{IsCJKUnifiedIdeographs}]{2,}(?:省|市|自治区|特别行政区)))", string.Empty);
        var brandIndex = new[] { "\"LENSPOP", "\"LEEA", "LENSPOP", "LEEA", "lenspop", "leea" }
            .Select(marker => address.IndexOf(marker, StringComparison.OrdinalIgnoreCase))
            .Where(index => index > 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (brandIndex > 0)
        {
            address = address[..brandIndex];
        }

        address = Regex.Replace(address, @"\s+", string.Empty);
        return address.Trim(' ', ',', ';');
    }

    private static string SanitizeName(string value)
    {
        value = CleanupFreeText(value);
        value = Regex.Replace(value, @"\s+", string.Empty);
        return value.Trim(' ', ',', ';');
    }

    private static string CleanupFreeText(string text)
    {
        var cleaned = BracketNoiseRegex.Replace(text, string.Empty);
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return cleaned.Trim(' ', ',', ';', ':');
    }

    private static string TrimForHint(string text)
    {
        return text.Length <= 48 ? text : text[..48] + "...";
    }

    private sealed record CanonicalAliasToken(string CanonicalName, string Alias, string CompactAlias);

    private sealed record ProductAliasToken(string CanonicalName, string Alias, string CompactAlias);

    private sealed class ParseIndex
    {
        public IReadOnlyList<CanonicalAliasToken> BrandAliases { get; init; } = Array.Empty<CanonicalAliasToken>();

        public IReadOnlyList<CanonicalAliasToken> WearAliases { get; init; } = Array.Empty<CanonicalAliasToken>();

        public IReadOnlyList<ProductAliasToken> ProductAliases { get; init; } = Array.Empty<ProductAliasToken>();

        public IReadOnlyList<string> KnownProductAliases { get; init; } = Array.Empty<string>();

        public static ParseIndex Create(ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries)
        {
            var productAliases = new List<ProductAliasToken>();
            var knownProductAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in ruleSet.ProductAliases)
            {
                foreach (var alias in rule.Aliases)
                {
                    if (!IsUsableProductAlias(alias))
                    {
                        continue;
                    }

                    var compactAlias = CompactForMatch(alias);
                    if (string.IsNullOrWhiteSpace(compactAlias))
                    {
                        continue;
                    }

                    productAliases.Add(new ProductAliasToken(rule.CanonicalName, alias, compactAlias));
                    knownProductAliases.Add(alias);
                }
            }

            foreach (var entry in catalogEntries ?? Array.Empty<ProductCatalogEntry>())
            {
                var canonicalName = GetCatalogDisplayName(entry);
                foreach (var alias in GetCatalogAliases(entry))
                {
                    var compactAlias = CompactForMatch(alias);
                    if (string.IsNullOrWhiteSpace(compactAlias))
                    {
                        continue;
                    }

                    productAliases.Add(new ProductAliasToken(canonicalName, alias, compactAlias));
                    knownProductAliases.Add(alias);
                }
            }

            return new ParseIndex
            {
                BrandAliases = BuildCanonicalAliasTokens(ruleSet.BrandAliases),
                WearAliases = BuildCanonicalAliasTokens(ruleSet.WearTypeAliases),
                ProductAliases = productAliases
                    .OrderByDescending(item => item.CompactAlias.Length)
                    .ToList(),
                KnownProductAliases = knownProductAliases
                    .OrderByDescending(item => item.Length)
                    .ToList()
            };
        }
    }

    private static List<CanonicalAliasToken> BuildCanonicalAliasTokens(IReadOnlyDictionary<string, List<string>> aliases)
    {
        return aliases
            .SelectMany(entry => entry.Value.Select(alias => new CanonicalAliasToken(entry.Key, alias, CompactForMatch(alias))))
            .Where(item => !string.IsNullOrWhiteSpace(item.CompactAlias))
            .OrderByDescending(item => item.CompactAlias.Length)
            .ToList();
    }
}

