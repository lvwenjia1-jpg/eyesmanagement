using System.Text.RegularExpressions;
using OrderTextTrainer.Core.Models;

namespace OrderTextTrainer.Core.Services;

public sealed class OrderTextParser
{
    private static readonly Regex PhoneRegex = new(@"(?:\+?86[- ]?)?((?:1[3-9]\d[- ]?\d{4}[- ]?\d{4})|(?:1[3-9]\d{9}))(?:[-转 ](\d{1,6}))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LandlineRegex = new(@"(?<!\d)(0\d{2,3}-?\d{7,8})(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExplicitFieldRegex = new(@"^(?<label>[^:]{1,8}):\s*(?<value>.+)$", RegexOptions.Compiled);
    private static readonly Regex SeparatorRegex = new(@"^[-=_*]{4,}$", RegexOptions.Compiled);
    private static readonly Regex BracketNoiseRegex = new(@"\[(?:\d{3,}|号码保护中[^\]]*)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] AccessoryKeywords =
    {
        "护理液", "护理盒", "伴侣盒", "盒子", "镜盒", "双联盒", "收纳盒", "眼镜盒",
        "吸棒", "镊子", "湿巾", "润眼液", "清洗器", "工具盒"
    };

    private readonly TextNormalizer _normalizer = new();

    public ParseResult Parse(string? rawText, ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries = null)
    {
        var normalized = _normalizer.Normalize(rawText);
        var result = new ParseResult { NormalizedText = normalized };
        if (string.IsNullOrWhiteSpace(normalized))
        {
            result.Warnings.Add("输入为空。");
            return result;
        }

        var blocks = SplitIntoOrderBlocks(normalized);
        foreach (var block in blocks)
        {
            var order = ParseSingleOrder(block, ruleSet, catalogEntries, result.UnknownSegments);
            if (order.Items.Count == 0)
            {
                result.Warnings.Add($"有一段文本未识别出商品：{TrimForHint(block)}");
            }

            result.Orders.Add(order);
        }

        return result;
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

    private List<string> SplitIntoOrderBlocks(string text)
    {
        var preBlocks = Regex.Split(text, @"(?:\n\s*\n)+")
            .Select(block => block.Trim())
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .ToList();

        if (preBlocks.Count > 1)
        {
            return preBlocks;
        }

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

    private ParsedOrder ParseSingleOrder(string block, ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries, ICollection<string> unknownSegments)
    {
        var order = new ParsedOrder { SourceText = block };
        var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var consumedLines = new HashSet<int>();

        order.Brand = FindCanonicalAlias(block, ruleSet.BrandAliases);
        order.DetectedWearPeriod = FindCanonicalAlias(block, ruleSet.WearTypeAliases);
        order.WearPeriod = order.DetectedWearPeriod;
        order.Phone = ExtractPhone(block);

        ExtractExplicitFields(lines, ruleSet, order, consumedLines);

        if (string.IsNullOrWhiteSpace(order.Address))
        {
            order.Address = GuessAddress(lines, ruleSet, consumedLines);
        }

        if (string.IsNullOrWhiteSpace(order.CustomerName))
        {
            order.CustomerName = GuessCustomerName(lines, ruleSet, order.Phone, order.Address, consumedLines);
        }

        ParseItems(lines, ruleSet, catalogEntries, order, consumedLines, unknownSegments);

        return order;
    }

    private void ExtractExplicitFields(List<string> lines, ParserRuleSet ruleSet, ParsedOrder order, ISet<int> consumedLines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var match = ExplicitFieldRegex.Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            var label = match.Groups["label"].Value.Trim();
            var value = CleanupFreeText(match.Groups["value"].Value.Trim());
            if (ruleSet.NameLabels.Any(item => label.Contains(item, StringComparison.OrdinalIgnoreCase)))
            {
                order.CustomerName = SanitizeName(value);
                consumedLines.Add(index);
                continue;
            }

            if (ruleSet.PhoneLabels.Any(item => label.Contains(item, StringComparison.OrdinalIgnoreCase)))
            {
                order.Phone = ExtractPhone(value) ?? value;
                consumedLines.Add(index);
                continue;
            }

            if (ruleSet.AddressLabels.Any(item => label.Contains(item, StringComparison.OrdinalIgnoreCase)))
            {
                order.Address = CleanupAddress(value);
                consumedLines.Add(index);
                continue;
            }

            if (label.Contains("赠", StringComparison.OrdinalIgnoreCase))
            {
                order.Gifts.Add(value);
                consumedLines.Add(index);
                continue;
            }

            if (label.Contains("备注", StringComparison.OrdinalIgnoreCase))
            {
                order.Remark = value;
                consumedLines.Add(index);
            }
        }
    }

    private string? GuessAddress(List<string> lines, ParserRuleSet ruleSet, ISet<int> consumedLines)
    {
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
        foreach (var line in lines)
        {
            var cleanedLine = CleanupFreeText(line);
            if (!string.IsNullOrWhiteSpace(phone) && PhoneRegex.IsMatch(cleanedLine))
            {
                var phoneMatch = PhoneRegex.Match(cleanedLine);
                if (phoneMatch.Success && phoneMatch.Index > 0)
                {
                    var beforePhone = cleanedLine[..phoneMatch.Index];
                    var leadingToken = beforePhone.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(SanitizeName)
                        .LastOrDefault(candidate => IsPossibleName(candidate, ruleSet));
                    if (!string.IsNullOrWhiteSpace(leadingToken))
                    {
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
                    .LastOrDefault(candidate => IsPossibleName(candidate, ruleSet));
                if (!string.IsNullOrWhiteSpace(token))
                {
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
            if (IsPossibleName(line, ruleSet))
            {
                consumedLines.Add(index);
                return line;
            }
        }

        return null;
    }

    private void ParseItems(List<string> lines, ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries, ParsedOrder order, ISet<int> consumedLines, ICollection<string> unknownSegments)
    {
        var segments = ExpandSegments(lines)
            .SelectMany(segment => SplitByKnownProducts(segment, ruleSet, catalogEntries))
            .ToList();

        for (var index = 0; index < segments.Count; index++)
        {
            var trimmedSegment = CleanupFreeText(segments[index].Trim());
            if (string.IsNullOrWhiteSpace(trimmedSegment) || SeparatorRegex.IsMatch(trimmedSegment))
            {
                continue;
            }

            TryFindOriginalLineIndex(lines, trimmedSegment, out var lineIndex);

            if (ruleSet.GiftKeywords.Any(keyword => trimmedSegment.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                order.Gifts.Add(trimmedSegment);
                if (lineIndex >= 0)
                {
                    consumedLines.Add(lineIndex);
                }
                continue;
            }

            if (LooksLikeGiftOrAccessoryLine(trimmedSegment, ruleSet, catalogEntries))
            {
                order.Gifts.Add(trimmedSegment);
                if (lineIndex >= 0)
                {
                    consumedLines.Add(lineIndex);
                }
                continue;
            }

            if (LooksLikeMetadata(trimmedSegment, ruleSet, catalogEntries, order))
            {
                continue;
            }

            var item = TryParseItem(trimmedSegment, ruleSet, catalogEntries);
            if (item is null)
            {
                if (LooksLikeProductCandidate(trimmedSegment))
                {
                    unknownSegments.Add(trimmedSegment);
                }
                continue;
            }

            order.Items.Add(item);
            if (item.IsOutOfStock)
            {
                order.OutOfStockLines.Add(trimmedSegment);
            }
        }
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

    private static IEnumerable<string> SplitByKnownProducts(string segment, ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries)
    {
        var knownAliases = GetKnownProductAliases(ruleSet, catalogEntries);
        var matches = knownAliases
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

    private OrderItem? TryParseItem(string segment, ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries)
    {
        var normalized = CleanupFreeText(segment.Trim('"', ' '));
        var productName = FindProductName(normalized, ruleSet, catalogEntries);
        if (string.IsNullOrWhiteSpace(productName))
        {
            return null;
        }

        if (LooksLikeNonProductDetail(normalized, productName))
        {
            return null;
        }

        var powers = ExtractPowers(normalized);
        var quantity = ExtractQuantity(normalized);
        var isTrial = normalized.Contains("试戴", StringComparison.OrdinalIgnoreCase) || normalized.Contains("试用", StringComparison.OrdinalIgnoreCase);
        var isOutOfStock = normalized.Contains("缺货", StringComparison.OrdinalIgnoreCase);

        var item = new OrderItem
        {
            RawText = normalized,
            ProductName = productName,
            Quantity = quantity,
            IsTrial = isTrial,
            IsOutOfStock = isOutOfStock
        };

        if (powers.Count >= 2)
        {
            item.LeftPower = powers[0];
            item.RightPower = powers[1];
            item.PowerSummary = $"{powers[0]}/{powers[1]}";
        }
        else if (powers.Count == 1)
        {
            item.LeftPower = powers[0];
            item.PowerSummary = powers[0];
        }

        return item;
    }

    private string? FindProductName(string text, ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries)
    {
        var compactText = CompactForMatch(text);
        var aliasMatches = ruleSet.ProductAliases
            .SelectMany(rule => rule.Aliases.Select(alias => new { CanonicalName = rule.CanonicalName, Alias = alias, CompactAlias = CompactForMatch(alias) }))
            .Where(item => AliasMatchesText(text, compactText, item.Alias, item.CompactAlias))
            .Select(item => new { item.CanonicalName, CompactAlias = item.CompactAlias })
            .ToList();

        var catalogMatches = GetKnownCatalogProductNames(catalogEntries)
            .Select(alias => new
            {
                CanonicalName = alias,
                Alias = alias,
                CompactAlias = CompactForMatch(alias)
            })
            .Where(item => AliasMatchesText(text, compactText, item.Alias, item.CompactAlias))
            .Select(item => new { item.CanonicalName, item.CompactAlias })
            .ToList();

        if (catalogMatches.Count == 0 && catalogEntries is not null)
        {
            catalogMatches = catalogEntries
                .Select(entry => new
                {
                    CanonicalName = GetCatalogDisplayName(entry),
                    Alias = GetLooseCatalogAlias(entry),
                    CompactAlias = CompactForMatch(GetLooseCatalogAlias(entry))
                })
                .Where(item => AliasMatchesText(text, compactText, item.Alias, item.CompactAlias))
                .Select(item => new { item.CanonicalName, item.CompactAlias })
                .ToList();
        }

        var match = aliasMatches
            .Concat(catalogMatches)
            .OrderByDescending(item => item.CompactAlias.Length)
            .FirstOrDefault();

        return match?.CanonicalName;
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
        stripped = Regex.Replace(stripped, @"[xX*]\s*\d+", " ", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"(?:试戴|试用|缺货)", " ", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"\s+", string.Empty);

        return Regex.IsMatch(stripped, @"^\d{1,4}(?:[/\-]\d{1,4})?$");
    }

    private static List<string> ExtractPowers(string text)
    {
        var slashMatch = Regex.Match(text, @"(?<![\d.])(\d{1,4})\s*/\s*(\d{1,4})(?!\d)");
        if (slashMatch.Success)
        {
            return new List<string> { slashMatch.Groups[1].Value, slashMatch.Groups[2].Value };
        }

        var degreeMatches = Regex.Matches(text, @"(\d{1,4})\s*度");
        if (degreeMatches.Count > 0)
        {
            return degreeMatches.Select(item => item.Groups[1].Value).ToList();
        }

        var powerBeforeQuantity = Regex.Match(text, @"(?<![\d.])(\d{1,4})(?=\s*(?:\d+|[一二两三四五六七八九十])\s*(?:副|盒|个|片))");
        if (powerBeforeQuantity.Success)
        {
            return new List<string> { powerBeforeQuantity.Groups[1].Value };
        }

        var candidate = Regex.Replace(text, @"\d+(?:\.\d+)?\s*mm", " ", RegexOptions.IgnoreCase);
        candidate = Regex.Replace(candidate, @"(?:共)?\s*(?:\d+|[一二两三四五六七八九十])\s*(?:副|盒|个|片)", " ");
        candidate = Regex.Replace(candidate, @"[xX*]\s*\d+", " ");
        candidate = Regex.Replace(candidate, @"缺货", " ", RegexOptions.IgnoreCase);

        var trailing = Regex.Match(candidate, @"(?<![\d.])(\d{1,4})(?!\d)");
        if (trailing.Success)
        {
            return new List<string> { trailing.Groups[1].Value };
        }

        return new List<string>();
    }

    private static int? ExtractQuantity(string text)
    {
        var match = Regex.Match(text, @"(?:共)?\s*(\d+)\s*(?:副|盒|个|片)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var quantity))
        {
            return quantity;
        }

        match = Regex.Match(text, @"[xX*]\s*(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out quantity))
        {
            return quantity;
        }

        var chineseQuantity = Regex.Match(text, @"([一二两三四五六七八九十])\s*(?:副|盒|个|片)");
        if (chineseQuantity.Success)
        {
            return ChineseNumberToInt(chineseQuantity.Groups[1].Value);
        }

        if (text.Contains("一副", StringComparison.OrdinalIgnoreCase) || text.Contains("一盒", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return null;
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

    private static bool LooksLikeMetadata(string segment, ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries, ParsedOrder order)
    {
        var containsProductAlias = ContainsKnownProductAlias(segment, ruleSet, catalogEntries);

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

    private static bool LooksLikeGiftOrAccessoryLine(string segment, ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries)
    {
        var cleaned = CleanupFreeText(segment);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        var containsProductAlias = ContainsKnownProductAlias(cleaned, ruleSet, catalogEntries);
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

        return Regex.IsMatch(segment, @"[\u4e00-\u9fa5A-Za-z]{2,}") &&
               (segment.Contains("度", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("/", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("片", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("盒", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("x", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(segment, @"\d{1,4}"));
    }

    private static string? FindCanonicalAlias(string text, IReadOnlyDictionary<string, List<string>> aliases)
    {
        var compactText = CompactForMatch(text);
        return aliases
            .SelectMany(entry => entry.Value.Select(alias => new { entry.Key, Alias = alias, CompactAlias = CompactForMatch(alias) }))
            .Where(item => !string.IsNullOrWhiteSpace(item.CompactAlias) && compactText.Contains(item.CompactAlias, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CompactAlias.Length)
            .Select(item => item.Key)
            .FirstOrDefault();
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

    private static bool ContainsKnownProductAlias(string segment, ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries)
    {
        var compactSegment = CompactForMatch(segment);
        if (ruleSet.ProductAliases.Any(rule => rule.Aliases.Any(alias => AliasMatchesText(segment, compactSegment, alias, CompactForMatch(alias)))))
        {
            return true;
        }

        return GetKnownCatalogProductNames(catalogEntries)
            .Any(alias => AliasMatchesText(segment, compactSegment, alias, CompactForMatch(alias)));
    }

    private static IEnumerable<string> GetKnownProductAliases(ParserRuleSet ruleSet, IReadOnlyList<ProductCatalogEntry>? catalogEntries)
    {
        foreach (var rule in ruleSet.ProductAliases)
        {
            foreach (var alias in rule.Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    yield return alias;
                }
            }
        }

        foreach (var alias in GetKnownCatalogProductNames(catalogEntries))
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                yield return alias;
            }
        }
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
        var candidates = new[]
        {
            GetCatalogDisplayName(entry),
            entry.ProductName,
            entry.BaseName,
            RemoveSpecificationPrefix(entry.BaseName, entry.SpecificationToken),
            RemoveSpecificationPrefix(entry.ProductName, entry.SpecificationToken),
            GetLooseCatalogAlias(entry)
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                yield return candidate;
            }
        }
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
        alias = Regex.Replace(alias, "(深蓝|浅蓝|棕|蓝|灰|粉|黄|绿|青|紫|黑|白|红|银|金)$", string.Empty, RegexOptions.IgnoreCase);
        return alias.Trim();
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

        address = Regex.Replace(address, @"\s+", " ");
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
}
