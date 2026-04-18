using OrderTextTrainer.Core.Models;
using OrderTextTrainer.Core.Services;
using System.Text;
using System.Text.RegularExpressions;

namespace WpfApp11;

public sealed class CatalogSkuResolver
{
    static CatalogSkuResolver()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void RefreshDrafts(IEnumerable<OrderDraft> drafts, WorkflowSettingsSnapshot snapshot)
    {
        var context = BuildResolverContext(snapshot);
        foreach (var draft in drafts)
        {
            RefreshDraft(draft, snapshot, context);
        }
    }

    public void RefreshDraft(OrderDraft draft, WorkflowSettingsSnapshot snapshot)
    {
        var context = BuildResolverContext(snapshot);
        RefreshDraft(draft, snapshot, context);
    }

    private void RefreshDraft(OrderDraft draft, WorkflowSettingsSnapshot snapshot, ResolverContext context)
    {
        foreach (var item in draft.Items)
        {
            RefreshItem(item, snapshot, context);
        }
    }

    public void RefreshItem(OrderItemDraft item, WorkflowSettingsSnapshot snapshot)
    {
        var context = BuildResolverContext(snapshot);
        RefreshItem(item, snapshot, context);
    }

    private void RefreshItem(OrderItemDraft item, WorkflowSettingsSnapshot snapshot, ResolverContext context)
    {
        var catalog = context.Catalog;
        InitializeSearchKeyword(item);
        item.ProductCodeOptions = new List<ProductCodeOption>();
        item.DegreeOptions = context.AllDegreeOptions.ToList();

        if (catalog.Count == 0)
        {
            item.MatchHint = "未导入商品列表，请先导入 Excel 商品表。";
            SetProductMatchState(item, "Unmatched", "未匹配");
            SetProductWorkflow(item, "待导入目录", "还没有商品编码目录，先导入商品列表。");
            FinalizeSearchState(item);
            return;
        }

        context.ByCode.TryGetValue(item.ProductCode, out var manualSelection);
        if (manualSelection is not null)
        {
            var manualRank = ScoreCandidate(
                manualSelection,
                BuildMatchContext(item, snapshot),
                snapshot,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            ApplyCatalogEntry(item, manualSelection, snapshot, "已按商品编码确认");
            var manualFamilyEntries = context.GetFamilyEntries(manualSelection);
            item.DegreeOptions = BuildDegreeOptions(manualFamilyEntries);
            item.ProductCodeOptions = BuildProductCodeOptions(catalog, new[] { manualRank }, manualFamilyEntries, item);
            if (item.ProductCodeConfirmed || manualRank.FieldMatchCount == 3)
            {
                item.ProductCodeConfirmed = true;
                SetProductMatchState(item, "Exact", "完全匹配");
                SetProductWorkflow(item, "已确认编码", item.ProductCodeConfirmed
                    ? "当前商品编码已经确认，后续仅在你手动修改时才会变化。"
                    : "识别条件与当前商品编码完全一致。");
            }
            else
            {
                SetProductMatchState(item, "Partial", "不完全匹配");
                SetProductWorkflow(item, "待复核", "当前商品编码已存在，但识别条件未完全对齐，请点开确认。");
            }
            FinalizeSearchState(item);
            return;
        }

        var aliasFamily = FindAliasFamily(item, context);
        var familyHint = aliasFamily ?? FindTopFamily(item, context);
        var rankedCandidates = RankCandidates(catalog, item, snapshot, familyHint);
        item.ProductCodeOptions = BuildProductCodeOptions(catalog, rankedCandidates, familyHint?.Entries, item);

        var preferredFamilyEntries = familyHint?.Entries;
        if ((preferredFamilyEntries is null || preferredFamilyEntries.Count == 0) && rankedCandidates.Count > 0 && rankedCandidates[0].FamilyMatched)
        {
            preferredFamilyEntries = context.GetFamilyEntries(rankedCandidates[0].Entry);
        }

        if (preferredFamilyEntries is not null && preferredFamilyEntries.Count > 0)
        {
            item.DegreeOptions = BuildDegreeOptions(preferredFamilyEntries);
            if (string.IsNullOrWhiteSpace(item.ProductName))
            {
                item.ProductName = GetFamilyDisplayName(preferredFamilyEntries[0]);
            }
        }

        var exactCandidates = rankedCandidates
            .Where(candidate => candidate.FieldMatchCount == 3)
            .ToList();
        if (exactCandidates.Count == 1)
        {
            ApplyCatalogEntry(item, exactCandidates[0].Entry, snapshot, exactCandidates[0].MatchNote);
            item.ProductCodeConfirmed = true;
            item.ProductCodeOptions = BuildProductCodeOptions(catalog, exactCandidates, preferredFamilyEntries, item);
            SetProductMatchState(item, "Exact", "完全匹配");
            SetProductWorkflow(item, "自动直配", "周期、型号、度数三项命中，已直接赋值商品编码。");
            FinalizeSearchState(item);
            return;
        }

        if (exactCandidates.Count > 1)
        {
            var bestExactCandidate = SelectBestExactCandidate(item, exactCandidates);
            ApplyCatalogEntry(item, bestExactCandidate.Entry, snapshot, $"{bestExactCandidate.MatchNote}，已按最相近商品编码自动选择");
            item.ProductCodeConfirmed = true;
            item.ProductCodeOptions = BuildProductCodeOptions(catalog, exactCandidates, preferredFamilyEntries, item);
            SetProductMatchState(item, "Exact", "完全匹配");
            SetProductWorkflow(item, "自动优选", $"周期、型号、度数三项命中，存在 {exactCandidates.Count} 个完全匹配候选，已自动选择最相近编码。");
            FinalizeSearchState(item);
            return;
        }

        var uniqueCandidate = SelectUniqueSuitableCandidate(rankedCandidates);
        if (uniqueCandidate is not null)
        {
            if (CanPromoteUniqueCandidateToExact(uniqueCandidate, rankedCandidates, item))
            {
                ApplyCatalogEntry(item, uniqueCandidate.Entry, snapshot, $"{uniqueCandidate.MatchNote}，两项条件命中且第三项唯一，已按完全匹配自动确认");
                item.ProductCodeConfirmed = true;
                item.ProductCodeOptions = BuildProductCodeOptions(catalog, rankedCandidates, preferredFamilyEntries, item);
                SetProductMatchState(item, "Exact", "完全匹配");
                SetProductWorkflow(item, "自动直配", "两项关键条件命中且第三项候选唯一，已按完全匹配自动确认。");
                FinalizeSearchState(item);
                return;
            }

            ApplyCatalogEntry(item, uniqueCandidate.Entry, snapshot, $"{uniqueCandidate.MatchNote}，已按唯一合适候选自动选中，待人工确认");
            item.ProductCodeConfirmed = false;
            item.ProductCodeOptions = BuildProductCodeOptions(catalog, rankedCandidates, preferredFamilyEntries, item);
            SetProductMatchState(item, "Partial", "待确认");
            SetProductWorkflow(item, "待人工确认", "当前仅存在一条合适候选，已自动选中商品编码，请复核后确认。");
            FinalizeSearchState(item);
            return;
        }

        if (rankedCandidates.Count > 0 && !rankedCandidates.Any(candidate => candidate.FamilyMatched))
        {
            item.ProductCode = string.Empty;
            item.ProductCodeConfirmed = false;
            SetProductMatchState(item, "Unmatched", "未匹配");
            item.MatchHint = "当前候选只命中周期或度数，未命中型号，未自动预选商品编码。";
            SetProductWorkflow(item, "待人工确认", "候选中没有型号命中，系统不会仅凭周期和度数自动套用其他型号。");
            FinalizeSearchState(item);
            return;
        }

        if (rankedCandidates.Count > 0)
        {
            var topCandidate = rankedCandidates[0];
            ApplyCatalogEntry(item, topCandidate.Entry, snapshot, $"{topCandidate.MatchNote}，已自动预选最可能候选，待人工确认");
            item.ProductCodeConfirmed = false;
            item.ProductCodeOptions = BuildProductCodeOptions(catalog, rankedCandidates, preferredFamilyEntries, item);
            SetProductMatchState(item, "Partial", "待确认");
            SetProductWorkflow(item, topCandidate.FieldMatchCount switch
            {
                2 => "自动预选",
                1 => "自动预选",
                _ => "自动预选"
            }, topCandidate.FieldMatchCount switch
            {
                2 => "已自动选中最可能编码（命中两项关键条件），请人工复核后确认。",
                1 => "已自动选中最可能编码（命中一项关键条件），请人工复核后确认。",
                _ => "已自动选中最可能编码，请人工复核后确认。"
            });
            FinalizeSearchState(item);
            return;
        }

        item.ProductCode = string.Empty;
        item.ProductCodeConfirmed = false;
        SetProductMatchState(item, "Unmatched", "未匹配");

        if (string.IsNullOrWhiteSpace(item.WearPeriod) && preferredFamilyEntries is not null && preferredFamilyEntries.Count > 0)
        {
            var periods = preferredFamilyEntries
                .Select(entry => ResolveCanonicalWearPeriod(entry.SpecificationToken, snapshot))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (periods.Count == 1)
            {
                item.WearPeriod = periods[0];
            }
        }

        if (rankedCandidates.Count == 0)
        {
            var recognizedCount = CountRecognizedConditions(item);
            item.MatchHint = familyHint is null
                ? "未匹配到商品，请输入关键词后模糊查询商品编码。"
                : $"{familyHint.MatchNote}，请继续输入周期/颜色/度数以缩小范围。";
            SetProductMatchState(item, "Unmatched", "未匹配");
            SetProductWorkflow(item,
                recognizedCount >= 2 ? "待补目录" : "待识别",
                recognizedCount >= 2
                    ? "已识别出关键条件，但目录里还没有命中编码，建议补目录或补别名。"
                    : "当前识别条件不足，建议补充周期、型号或度数。");
            FinalizeSearchState(item);
            return;
        }

        FinalizeSearchState(item);
    }

    private static CatalogFamilyMatch? FindAliasFamily(
        OrderItemDraft item,
        ResolverContext context)
    {
        var compactTokens = BuildCompactTokens(item);
        var aliasMatch = context.ProductCodeAliases
            .Select(alias => new
            {
                alias.Row,
                alias.CompactAlias,
                Score = compactTokens.Any(token => token.Contains(alias.CompactAlias, StringComparison.OrdinalIgnoreCase))
                    ? alias.CompactAlias.Length
                    : 0
            })
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .FirstOrDefault();

        if (aliasMatch is null)
        {
            return null;
        }

        if (!context.ByCode.TryGetValue(aliasMatch.Row.ProductCode, out var representative))
        {
            return null;
        }

        var familyEntries = context.GetFamilyEntries(representative);
        if (familyEntries.Count == 0)
        {
            return null;
        }

        return new CatalogFamilyMatch(familyEntries, $"编码映射已锁定款式：{aliasMatch.Row.Alias}");
    }

    private static CatalogFamilyMatch? FindTopFamily(OrderItemDraft item, ResolverContext context)
    {
        var compactTokens = BuildCompactTokens(item);
        if (compactTokens.Count == 0)
        {
            return null;
        }

        var directLooseFamily = FindDirectLooseFamily(item, context);
        if (directLooseFamily is not null)
        {
            return directLooseFamily;
        }

        var rankedFamilies = context.ByFamily.Values
            .Select(entries => new CatalogFamilyRank(entries, ScoreFamily(entries, compactTokens)))
            .Where(rank => rank.Score > 0)
            .OrderByDescending(rank => rank.Score)
            .ThenByDescending(rank => MatchTextHelper.Compact(GetFamilyDisplayName(rank.Entries[0])).Length)
            .Take(3)
            .ToList();

        if (rankedFamilies.Count == 0)
        {
            return null;
        }

        if (rankedFamilies.Count > 1 && rankedFamilies[0].Score == rankedFamilies[1].Score)
        {
            var looseFamilies = context.ByLooseFamily.Values
                .Select(entries => new CatalogFamilyRank(entries, ScoreFamily(entries, compactTokens)))
                .Where(rank => rank.Score > 0)
                .OrderByDescending(rank => rank.Score)
                .ThenByDescending(rank => MatchTextHelper.Compact(GetFamilyDisplayName(rank.Entries[0])).Length)
                .Take(3)
                .ToList();

            if (looseFamilies.Count == 0 || (looseFamilies.Count > 1 && looseFamilies[0].Score == looseFamilies[1].Score))
            {
                return null;
            }

            return new CatalogFamilyMatch(looseFamilies[0].Entries, "商品列表已匹配到同系列候选");
        }

        return new CatalogFamilyMatch(rankedFamilies[0].Entries, "商品列表已匹配到款式候选");
    }

    private static CatalogFamilyMatch? FindDirectLooseFamily(OrderItemDraft item, ResolverContext context)
    {
        var directTokens = new[]
            {
                item.ProductName,
                RemoveDigits(item.ProductName)
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(MatchTextHelper.Compact)
            .Where(value => value.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(value => value.Length)
            .ToList();

        foreach (var token in directTokens)
        {
            var exactGroups = context.ByLooseFamily
                .Where(group => !string.IsNullOrWhiteSpace(group.Key) &&
                                (string.Equals(group.Key, token, StringComparison.OrdinalIgnoreCase) ||
                                 group.Key.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                                 token.Contains(group.Key, StringComparison.OrdinalIgnoreCase)))
                .Select(group => group.Value)
                .ToList();

            if (exactGroups.Count == 1)
            {
                return new CatalogFamilyMatch(exactGroups[0], "商品名称已匹配到系列候选");
            }
        }

        return null;
    }

    private static bool IsWearCompatible(ProductCatalogEntry entry, string wearPeriod, WorkflowSettingsSnapshot snapshot)
    {
        var left = MatchTextHelper.Compact(wearPeriod);
        var right = MatchTextHelper.Compact(entry.SpecificationToken);
        var preferredPackCount = DetectDailyPackCount(wearPeriod, defaultDailyToTwo: false);
        var entryPackCount = DetectEntryDailyPackCount(entry);

        if (preferredPackCount.HasValue && entryPackCount.HasValue && preferredPackCount.Value != entryPackCount.Value)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var matched = ResolveCanonicalWearPeriod(entry.SpecificationToken, snapshot);
        var canonical = MatchTextHelper.Compact(matched);
        return !string.IsNullOrWhiteSpace(canonical) &&
               (string.Equals(left, canonical, StringComparison.OrdinalIgnoreCase) ||
                left.Contains(canonical, StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyCatalogEntry(
        OrderItemDraft item,
        ProductCatalogEntry entry,
        WorkflowSettingsSnapshot snapshot,
        string note)
    {
        item.ProductCode = entry.ProductCode;
        item.ProductName = string.IsNullOrWhiteSpace(entry.ProductName) ? entry.ProductCode : entry.ProductName;
        item.SpecCodeText = Safe(entry.SpecCode);
        item.BarcodeText = Safe(entry.Barcode);
        item.DegreeText = string.IsNullOrWhiteSpace(entry.Degree) ? item.DegreeText : entry.Degree;

        if (string.IsNullOrWhiteSpace(item.WearPeriod))
        {
            item.WearPeriod = ResolveCanonicalWearPeriod(entry.SpecificationToken, snapshot);
        }

        item.MatchHint = $"{note}，已自动回填商品编码。";
    }

    private static string ResolveCanonicalWearPeriod(string specificationToken, WorkflowSettingsSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(specificationToken))
        {
            return string.Empty;
        }

        var compactSpecification = MatchTextHelper.Compact(specificationToken);
        var directWearPeriod = snapshot.WearPeriods
            .Select(item => item.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderByDescending(value => MatchTextHelper.Compact(value).Length)
            .FirstOrDefault(value =>
            {
                var compactValue = MatchTextHelper.Compact(value);
                return string.Equals(compactSpecification, compactValue, StringComparison.OrdinalIgnoreCase) ||
                       compactSpecification.Contains(compactValue, StringComparison.OrdinalIgnoreCase);
            });

        if (!string.IsNullOrWhiteSpace(directWearPeriod))
        {
            return directWearPeriod;
        }

        var mapping = snapshot.WearPeriodMappings.FirstOrDefault(item =>
        {
            var compactAlias = MatchTextHelper.Compact(item.Alias);
            return !string.IsNullOrWhiteSpace(compactAlias) &&
                   (string.Equals(compactSpecification, compactAlias, StringComparison.OrdinalIgnoreCase) ||
                    compactSpecification.Contains(compactAlias, StringComparison.OrdinalIgnoreCase));
        });

        return !string.IsNullOrWhiteSpace(mapping?.WearPeriod) ? mapping.WearPeriod : specificationToken;
    }

    private static int ScoreFamily(IReadOnlyList<ProductCatalogEntry> entries, IReadOnlyList<string> compactTokens)
    {
        var sample = entries[0];
        var aliases = GetFamilyAliases(sample)
            .Select(MatchTextHelper.Compact)
            .Where(value => !string.IsNullOrWhiteSpace(value) &&
                            value.Length >= 3 &&
                            IsModelLikeToken(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var score = 0;

        foreach (var token in compactTokens)
        {
            if (string.IsNullOrWhiteSpace(token) || !IsModelLikeToken(token))
            {
                continue;
            }

            var best = 0;
            foreach (var alias in aliases)
            {
                if (string.Equals(token, alias, StringComparison.OrdinalIgnoreCase))
                {
                    best = Math.Max(best, 160);
                    continue;
                }

                if (token.Contains(alias, StringComparison.OrdinalIgnoreCase))
                {
                    best = Math.Max(best, alias.Length >= 6 ? 120 : 70);
                    continue;
                }

                if (token.Length >= 4 && alias.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    best = Math.Max(best, token.Length >= 6 ? 100 : 55);
                }
            }

            score += best;
        }

        return score;
    }

    private static bool IsModelLikeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (Regex.IsMatch(token, @"^\d+$", RegexOptions.IgnoreCase))
        {
            return false;
        }

        if (Regex.IsMatch(token, @"^[0-9xX×＊\*]+$", RegexOptions.IgnoreCase))
        {
            return false;
        }

        return Regex.IsMatch(token, @"[\p{IsCJKUnifiedIdeographs}A-Za-z]", RegexOptions.IgnoreCase);
    }

    private static MatchContext BuildMatchContext(OrderItemDraft item, WorkflowSettingsSnapshot snapshot)
    {
        var compactTokens = BuildCompactTokens(item);
        var degreeKey = ResolveDraftDegreeKey(item);
        var wearPeriod = DetectWearPeriod(item, snapshot);
        var preferredDailyPackCount = DetectPreferredDailyPackCount(item, wearPeriod);
        return new MatchContext(compactTokens, degreeKey, wearPeriod, preferredDailyPackCount);
    }

    private static string ResolveDraftDegreeKey(OrderItemDraft item)
    {
        var preferredSource = string.IsNullOrWhiteSpace(item.DegreeText) ? item.SourceText : item.DegreeText;
        var explicitDegree = MatchTextHelper.ExtractExplicitDegreeKey(preferredSource);
        if (!string.IsNullOrWhiteSpace(explicitDegree))
        {
            return explicitDegree;
        }

        return MatchTextHelper.NormalizeDegreeKey(preferredSource);
    }

    private static string DetectWearPeriod(OrderItemDraft item, WorkflowSettingsSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(item.WearPeriod))
        {
            return item.WearPeriod.Trim();
        }

        var sources = new[]
            {
                item.WearPeriod,
                item.ProductCodeSearchKeyword,
                item.ProductName,
                item.SourceText
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        var direct = snapshot.WearPeriods
            .Select(value => value.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderByDescending(value => MatchTextHelper.Compact(value).Length)
            .FirstOrDefault(value => sources.Any(source =>
                MatchTextHelper.Compact(source).Contains(MatchTextHelper.Compact(value), StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var mapping = snapshot.WearPeriodMappings
            .Where(row => !string.IsNullOrWhiteSpace(row.Alias) && !string.IsNullOrWhiteSpace(row.WearPeriod))
            .OrderByDescending(row => MatchTextHelper.Compact(row.Alias).Length)
            .FirstOrDefault(row => sources.Any(source =>
                MatchTextHelper.Compact(source).Contains(MatchTextHelper.Compact(row.Alias), StringComparison.OrdinalIgnoreCase)));

        return mapping?.WearPeriod?.Trim() ?? string.Empty;
    }

    private static int? DetectPreferredDailyPackCount(OrderItemDraft item, string wearPeriod)
    {
        foreach (var source in new[]
                 {
                     item.WearPeriod,
                     wearPeriod,
                     item.ProductCodeSearchKeyword,
                     item.ProductName,
                     item.SourceText
                 })
        {
            var explicitPackCount = DetectDailyPackCount(source, defaultDailyToTwo: false);
            if (explicitPackCount.HasValue)
            {
                return explicitPackCount;
            }
        }

        foreach (var source in new[]
                 {
                     item.WearPeriod,
                     wearPeriod,
                     item.ProductCodeSearchKeyword,
                     item.ProductName,
                     item.SourceText
                 })
        {
            var defaultedPackCount = DetectDailyPackCount(source, defaultDailyToTwo: true);
            if (defaultedPackCount.HasValue)
            {
                return defaultedPackCount;
            }
        }

        return null;
    }

    private static int? DetectEntryDailyPackCount(ProductCatalogEntry entry)
    {
        foreach (var source in new[]
                 {
                     entry.SpecificationToken,
                     entry.ProductName,
                     entry.BaseName,
                     entry.ModelToken,
                     entry.ProductCode
                 })
        {
            var packCount = DetectDailyPackCount(source, defaultDailyToTwo: false);
            if (packCount.HasValue)
            {
                return packCount;
            }
        }

        return null;
    }

    private static int? DetectDailyPackCount(string? source, bool defaultDailyToTwo)
    {
        var text = Safe(source);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (Regex.IsMatch(text, @"(?:10片|十片)", RegexOptions.IgnoreCase))
        {
            return 10;
        }

        if (Regex.IsMatch(text, @"(?:2片|两片|試戴|试戴)", RegexOptions.IgnoreCase))
        {
            return 2;
        }

        return defaultDailyToTwo && text.Contains("日抛", StringComparison.OrdinalIgnoreCase)
            ? 2
            : null;
    }

    private static List<CatalogEntryMatch> RankCandidates(
        IReadOnlyList<ProductCatalogEntry> catalog,
        OrderItemDraft item,
        WorkflowSettingsSnapshot snapshot,
        CatalogFamilyMatch? familyHint)
    {
        var context = BuildMatchContext(item, snapshot);
        var familyHintCodes = new HashSet<string>(
            (familyHint?.Entries ?? Array.Empty<ProductCatalogEntry>())
                .Select(entry => entry.ProductCode),
            StringComparer.OrdinalIgnoreCase);

        return catalog
            .Select(entry => ScoreCandidate(entry, context, snapshot, familyHintCodes))
            .Where(match => match.Score > 0 || match.FieldMatchCount > 0 || familyHintCodes.Contains(match.Entry.ProductCode))
            .OrderByDescending(match => match.FieldMatchCount)
            .ThenByDescending(match => match.Score)
            .ThenByDescending(match => match.FamilyScore)
            .ThenBy(match => ParseDegree(Safe(match.Entry.Degree)))
            .ThenBy(match => match.Entry.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static CatalogEntryMatch ScoreCandidate(
        ProductCatalogEntry entry,
        MatchContext context,
        WorkflowSettingsSnapshot snapshot,
        IReadOnlySet<string> familyHintCodes)
    {
        var familyScore = ScoreEntryFamily(entry, context.CompactTokens);
        var familyMatched = familyScore >= 60 || familyHintCodes.Contains(entry.ProductCode);
        var degreeMatched = !string.IsNullOrWhiteSpace(context.DegreeKey) &&
                            string.Equals(MatchTextHelper.NormalizeDegreeKey(entry.Degree), context.DegreeKey, StringComparison.OrdinalIgnoreCase);
        var wearMatched = !string.IsNullOrWhiteSpace(context.WearPeriod) &&
                          IsWearCompatible(entry, context.WearPeriod, snapshot);

        var fieldMatchCount = 0;
        if (familyMatched)
        {
            fieldMatchCount++;
        }

        if (wearMatched)
        {
            fieldMatchCount++;
        }

        if (degreeMatched)
        {
            fieldMatchCount++;
        }

        var score = familyScore;
        if (familyMatched)
        {
            score += 120;
        }

        if (wearMatched)
        {
            score += 90;
        }

        if (degreeMatched)
        {
            score += 80;
        }

        if (familyMatched && degreeMatched)
        {
            score += 70;
        }

        if (familyMatched && wearMatched)
        {
            score += 50;
        }

        if (wearMatched && degreeMatched)
        {
            score += 20;
        }

        var entryPackCount = DetectEntryDailyPackCount(entry);
        if (context.PreferredDailyPackCount.HasValue && entryPackCount.HasValue)
        {
            if (context.PreferredDailyPackCount.Value == entryPackCount.Value)
            {
                score += 40;
            }
            else
            {
                score -= 120;
            }
        }

        if (familyHintCodes.Contains(entry.ProductCode))
        {
            score += 35;
        }

        if (fieldMatchCount == 0 && familyScore < 45)
        {
            score = 0;
        }

        return new CatalogEntryMatch(
            entry,
            familyMatched,
            wearMatched,
            degreeMatched,
            fieldMatchCount,
            familyScore,
            score,
            BuildMatchNote(familyMatched, wearMatched, degreeMatched));
    }

    private static int ScoreEntryFamily(ProductCatalogEntry entry, IReadOnlyList<string> compactTokens)
    {
        return ScoreFamily(new[] { entry }, compactTokens);
    }

    private static CatalogEntryMatch SelectBestExactCandidate(
        OrderItemDraft item,
        IReadOnlyList<CatalogEntryMatch> exactCandidates)
    {
        var baseSearchNames = BuildBaseSearchNames(item)
            .Select(MatchTextHelper.Compact)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return exactCandidates
            .OrderByDescending(candidate => ScoreExactCandidatePrecision(candidate.Entry, baseSearchNames))
            .ThenByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.FamilyScore)
            .ThenBy(candidate => candidate.Entry.ProductCode, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static CatalogEntryMatch? SelectUniqueSuitableCandidate(IReadOnlyList<CatalogEntryMatch> rankedCandidates)
    {
        if (rankedCandidates.Count == 0)
        {
            return null;
        }

        if (rankedCandidates.Count == 1 && rankedCandidates[0].FamilyMatched && rankedCandidates[0].FieldMatchCount >= 1)
        {
            return rankedCandidates[0];
        }

        var familyMatchedCandidates = rankedCandidates
            .Where(candidate => candidate.FamilyMatched)
            .ToList();
        if (familyMatchedCandidates.Count == 1 && familyMatchedCandidates[0].FieldMatchCount >= 1)
        {
            return familyMatchedCandidates[0];
        }

        var bestFieldMatchCount = rankedCandidates[0].FieldMatchCount;
        if (bestFieldMatchCount < 2)
        {
            return null;
        }

        var bestFieldCandidates = rankedCandidates
            .Where(candidate => candidate.FieldMatchCount == bestFieldMatchCount && candidate.FamilyMatched)
            .ToList();
        if (bestFieldCandidates.Count == 1)
        {
            return bestFieldCandidates[0];
        }

        // 只有命中型号时，才允许按“两项命中”自动预选，避免仅凭周期和度数误套其他型号。
        if (rankedCandidates[0].FieldMatchCount >= 2 && rankedCandidates[0].FamilyMatched)
        {
            return rankedCandidates[0];
        }

        var top = rankedCandidates[0];
        var second = rankedCandidates.Count > 1 ? rankedCandidates[1] : null;
        if (second is null)
        {
            return top.FamilyMatched && top.FieldMatchCount >= 1 ? top : null;
        }

        var scoreGap = top.Score - second.Score;
        if (top.FamilyMatched && top.FieldMatchCount >= 1 && scoreGap >= 60)
        {
            return top;
        }

        if (top.FamilyMatched && top.DegreeMatched && !second.DegreeMatched)
        {
            return top;
        }

        return null;
    }

    private static bool CanPromoteUniqueCandidateToExact(
        CatalogEntryMatch uniqueCandidate,
        IReadOnlyList<CatalogEntryMatch> rankedCandidates,
        OrderItemDraft item)
    {
        if (uniqueCandidate.FieldMatchCount != 2 || !uniqueCandidate.FamilyMatched)
        {
            return false;
        }

        var wearKnown = !string.IsNullOrWhiteSpace(item.WearPeriod);
        var degreeKnown = !string.IsNullOrWhiteSpace(item.DegreeText);

        if (uniqueCandidate.DegreeMatched && !uniqueCandidate.WearMatched && !wearKnown)
        {
            return rankedCandidates.Count(candidate => candidate.FamilyMatched && candidate.DegreeMatched) == 1;
        }

        if (uniqueCandidate.WearMatched && !uniqueCandidate.DegreeMatched && !degreeKnown)
        {
            return rankedCandidates.Count(candidate => candidate.FamilyMatched && candidate.WearMatched) == 1;
        }

        return false;
    }

    private static int ScoreExactCandidatePrecision(
        ProductCatalogEntry entry,
        IReadOnlyList<string> baseSearchNames)
    {
        if (baseSearchNames.Count == 0)
        {
            return 0;
        }

        var aliases = GetFamilyAliases(entry)
            .Select(MatchTextHelper.Compact)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var searchText = BuildOptionSearchText(entry);
        var score = 0;

        foreach (var token in baseSearchNames)
        {
            var best = 0;

            foreach (var alias in aliases)
            {
                if (string.Equals(token, alias, StringComparison.OrdinalIgnoreCase))
                {
                    best = Math.Max(best, 240 + alias.Length);
                    continue;
                }

                if (alias.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    best = Math.Max(best, 180 + token.Length);
                    continue;
                }

                if (token.Contains(alias, StringComparison.OrdinalIgnoreCase))
                {
                    best = Math.Max(best, 140 + alias.Length);
                }
            }

            if (best == 0 && searchText.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                best = 100 + token.Length;
            }

            score += best;
        }

        return score;
    }

    private static string BuildMatchNote(bool familyMatched, bool wearMatched, bool degreeMatched)
    {
        var parts = new List<string>();
        if (familyMatched)
        {
            parts.Add("款式");
        }

        if (wearMatched)
        {
            parts.Add("周期");
        }

        if (degreeMatched)
        {
            parts.Add("度数");
        }

        return parts.Count == 0
            ? "已找到候选商品"
            : $"已匹配{string.Join('、', parts)}";
    }

    private static List<string> BuildCompactTokens(OrderItemDraft item)
    {
        var tokens = new List<string>();
        var baseNames = BuildBaseSearchNames(item);
        var degrees = ExtractDegrees(item);

        foreach (var baseName in baseNames)
        {
            AddCompactToken(tokens, baseName);
            foreach (var degree in degrees)
            {
                AddCompactToken(tokens, $"{baseName}{degree}");
            }
        }

        AddCompactToken(tokens, item.ProductName);
        AddCompactToken(tokens, item.SourceText);

        return tokens
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void InitializeSearchKeyword(OrderItemDraft item)
    {
        if (!string.IsNullOrWhiteSpace(item.ProductCode))
        {
            item.ProductCodeSearchKeyword = item.ProductCode.Trim();
            return;
        }

        if (!string.IsNullOrWhiteSpace(item.ProductCodeSearchKeyword))
        {
            item.ProductCodeSearchKeyword = item.ProductCodeSearchKeyword.Trim();
            return;
        }

        item.ProductCodeSearchKeyword = Safe(item.ProductName);
    }

    private static void FinalizeSearchState(OrderItemDraft item)
    {
        if (!string.IsNullOrWhiteSpace(item.ProductCode))
        {
            item.ProductCodeSearchKeyword = item.ProductCode.Trim();
        }

        item.ProductCodeSearchSummary = BuildSearchSummary(item);
    }

    private static void SetProductMatchState(OrderItemDraft item, string state, string text)
    {
        item.ProductMatchState = state;
        item.ProductMatchStatusText = text;
    }

    private static void SetProductWorkflow(OrderItemDraft item, string stage, string detail)
    {
        item.ProductWorkflowStage = stage;
        item.ProductWorkflowDetail = detail;
    }

    private static string BuildSearchSummary(OrderItemDraft item)
    {
        var workflowPrefix = string.IsNullOrWhiteSpace(item.ProductWorkflowStage)
            ? string.Empty
            : $"[{item.ProductWorkflowStage}] ";

        if (!string.IsNullOrWhiteSpace(item.ProductCode))
        {
            var selected = item.ProductCodeOptions.FirstOrDefault(option =>
                string.Equals(option.ProductCode, item.ProductCode, StringComparison.OrdinalIgnoreCase));
            return selected is null
                ? $"{workflowPrefix}已选编码: {item.ProductCode}"
                : $"{workflowPrefix}已选: {BuildOptionSummary(selected)}";
        }

        var recognizedSummary = BuildRecognizedConditionSummary(item);
        if (item.ProductCodeOptions.Count == 0)
        {
            return string.IsNullOrWhiteSpace(recognizedSummary)
                ? $"{workflowPrefix}可按周期 / 型号 / 度数搜索商品编码。"
                : $"{workflowPrefix}已识别: {recognizedSummary}；当前目录未命中商品编码。";
        }

        var topOptions = item.ProductCodeOptions
            .Take(3)
            .Select(BuildOptionSummary)
            .ToList();

        return item.ProductCodeOptions.Count == 1
            ? (string.IsNullOrWhiteSpace(recognizedSummary)
                ? $"{workflowPrefix}唯一候选: {topOptions[0]}"
                : $"{workflowPrefix}已识别: {recognizedSummary}；唯一候选: {topOptions[0]}")
            : (string.IsNullOrWhiteSpace(recognizedSummary)
                ? $"{workflowPrefix}候选 {item.ProductCodeOptions.Count} 个: {string.Join("；", topOptions)}"
                : $"{workflowPrefix}已识别: {recognizedSummary}；候选 {item.ProductCodeOptions.Count} 个: {string.Join("；", topOptions)}");
    }

    private static string BuildRecognizedConditionSummary(OrderItemDraft item)
    {
        return string.Join(" / ", EnumerateRecognizedConditions(item));
    }

    private static int CountRecognizedConditions(OrderItemDraft item)
    {
        return EnumerateRecognizedConditions(item).Count;
    }

    private static List<string> EnumerateRecognizedConditions(OrderItemDraft item)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(item.WearPeriod))
        {
            parts.Add($"周期 {item.WearPeriod.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(item.ProductName))
        {
            parts.Add($"型号 {item.ProductName.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(item.DegreeText))
        {
            parts.Add($"度数 {item.DegreeText.Trim()}");
        }

        return parts;
    }

    private static string BuildOptionSummary(ProductCodeOption option)
    {
        var summary = string.Join(" / ", new[]
        {
            option.WearPeriod,
            option.ModelName,
            string.IsNullOrWhiteSpace(option.DegreeText) ? string.Empty : $"{option.DegreeText}度"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return string.IsNullOrWhiteSpace(summary) ? option.ProductCode : summary;
    }

    private static List<string> BuildDegreeOptions(IEnumerable<ProductCatalogEntry> entries)
    {
        return entries
            .Select(entry => Safe(entry.Degree))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => ParseDegree(value))
            .ToList();
    }

    private static List<ProductCodeOption> BuildProductCodeOptions(
        IReadOnlyList<ProductCatalogEntry> catalog,
        IReadOnlyList<CatalogEntryMatch> rankedCandidates,
        IReadOnlyList<ProductCatalogEntry>? familyEntries,
        OrderItemDraft? confirmedItem = null)
    {
        var rankedByCode = rankedCandidates
            .GroupBy(match => match.Entry.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(group => group.Entry.ProductCode, group => group, StringComparer.OrdinalIgnoreCase);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedEntries = new List<ProductCatalogEntry>();

        foreach (var entry in rankedCandidates.Select(match => match.Entry))
        {
            if (seenCodes.Add(entry.ProductCode))
            {
                orderedEntries.Add(entry);
            }
        }

        foreach (var entry in familyEntries ?? Array.Empty<ProductCatalogEntry>())
        {
            if (seenCodes.Add(entry.ProductCode))
            {
                orderedEntries.Add(entry);
            }
        }

        var shouldAppendAllCatalog = orderedEntries.Count == 0;
        foreach (var entry in catalog
                     .Where(entry => shouldAppendAllCatalog && seenCodes.Add(entry.ProductCode))
                     .OrderBy(entry => GetCoreCode(entry), StringComparer.OrdinalIgnoreCase)
                     .ThenBy(entry => ParseDegree(Safe(entry.Degree)))
                     .ThenBy(entry => entry.ProductCode, StringComparer.OrdinalIgnoreCase))
        {
            orderedEntries.Add(entry);
        }

        var options = new List<ProductCodeOption>(orderedEntries.Count);
        for (var index = 0; index < orderedEntries.Count; index++)
        {
            var entry = orderedEntries[index];
            rankedByCode.TryGetValue(entry.ProductCode, out var rankedMatch);
            var isConfirmedMatch = confirmedItem is not null &&
                                   confirmedItem.ProductCodeConfirmed &&
                                   string.Equals(confirmedItem.ProductCode, entry.ProductCode, StringComparison.OrdinalIgnoreCase);
            var (matchState, matchStateText) = ResolveOptionMatchState(isConfirmedMatch, rankedMatch);
            options.Add(new ProductCodeOption
            {
                ProductCode = entry.ProductCode,
                CoreCode = GetCoreCode(entry),
                WearPeriod = Safe(entry.SpecificationToken),
                ModelName = GetFamilyDisplayName(entry).Trim(),
                DegreeText = Safe(entry.Degree),
                DisplayText = BuildOptionDisplayText(entry),
                SearchText = BuildOptionSearchText(entry),
                Initials = BuildOptionInitials(entry),
                SortOrder = index,
                MatchScore = rankedMatch?.Score ?? 0,
                MatchFieldCount = rankedMatch?.FieldMatchCount ?? 0,
                MatchState = matchState,
                MatchStateText = matchStateText
            });
        }

        return options;
    }

    private static (string MatchState, string MatchStateText) ResolveOptionMatchState(
        bool isConfirmedMatch,
        CatalogEntryMatch? rankedMatch)
    {
        if (isConfirmedMatch || rankedMatch?.FieldMatchCount == 3)
        {
            return ("Exact", "完全匹配");
        }

        return rankedMatch is null
            ? ("Unmatched", "未匹配")
            : ("Partial", "不完全匹配");
    }

    private static string GetFamilyKey(ProductCatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ModelToken))
        {
            return MatchTextHelper.Compact(entry.ModelToken);
        }

        if (!string.IsNullOrWhiteSpace(entry.BaseName))
        {
            return MatchTextHelper.Compact(entry.BaseName);
        }

        return MatchTextHelper.Compact(entry.ProductCode);
    }

    private static string GetCoreCode(ProductCatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.SpecCode))
        {
            return entry.SpecCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.ProductName))
        {
            return entry.ProductName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.BaseName))
        {
            return entry.BaseName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.ModelToken))
        {
            return entry.ModelToken.Trim();
        }

        return entry.ProductCode.Trim();
    }

    private static string BuildOptionDisplayText(ProductCatalogEntry entry)
    {
        var parts = new List<string> { entry.ProductCode.Trim() };
        var familyDisplayName = GetFamilyDisplayName(entry);

        if (!string.IsNullOrWhiteSpace(entry.SpecificationToken))
        {
            parts.Add($"周期 {entry.SpecificationToken.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(familyDisplayName))
        {
            parts.Add($"型号 {familyDisplayName.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Degree))
        {
            parts.Add($"度数 {entry.Degree.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Barcode))
        {
            parts.Add(entry.Barcode.Trim());
        }

        return string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildOptionSearchText(ProductCatalogEntry entry)
    {
        return MatchTextHelper.Compact(string.Join(" ",
            new[]
            {
                entry.ProductCode,
                entry.ProductName,
                entry.BaseName,
                entry.ModelToken,
                entry.SpecificationToken,
                entry.SpecCode,
                entry.Degree,
                entry.Barcode,
                entry.SearchText,
                GetCoreCode(entry),
                GetFamilyDisplayName(entry)
            }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private static string BuildOptionInitials(ProductCatalogEntry entry)
    {
        return string.Concat(new[]
            {
                entry.ProductCode,
                entry.ProductName,
                entry.BaseName,
                entry.ModelToken,
                entry.SpecificationToken,
                GetCoreCode(entry)
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(ExtractInitials))
            .ToLowerInvariant();
    }

    private static string GetLooseFamilyKey(ProductCatalogEntry entry)
    {
        return MatchTextHelper.Compact(RemoveColorSuffix(RemoveSpecificationPrefix(GetFamilyDisplayName(entry), entry.SpecificationToken)));
    }

    private static string GetFamilyDisplayName(ProductCatalogEntry entry)
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

    private static IEnumerable<string> GetFamilyAliases(ProductCatalogEntry entry)
    {
        var displayName = GetFamilyDisplayName(entry);
        var looseAlias = RemoveColorSuffix(RemoveSpecificationPrefix(displayName, entry.SpecificationToken));
        var baseAlias = RemoveColorSuffix(RemoveSpecificationPrefix(entry.BaseName, entry.SpecificationToken));
        var productCodeAlias = RemoveSpecificationPrefix(entry.ProductCode, entry.SpecificationToken);
        var displayWithoutDegree = MatchTextHelper.RemoveTrailingDegree(displayName);
        var baseWithoutDegree = MatchTextHelper.RemoveTrailingDegree(entry.BaseName);
        var productCodeWithoutDegree = MatchTextHelper.RemoveTrailingDegree(productCodeAlias);

        foreach (var value in new[]
                 {
                     displayName,
                     entry.ProductName,
                     entry.BaseName,
                     entry.ProductCode,
                     RemoveSpecificationPrefix(entry.ProductName, entry.SpecificationToken),
                     RemoveSpecificationPrefix(entry.BaseName, entry.SpecificationToken),
                     productCodeAlias,
                     displayWithoutDegree,
                     baseWithoutDegree,
                     productCodeWithoutDegree,
                     RemoveProMarker(displayName),
                     RemoveProMarker(entry.ProductName),
                     RemoveProMarker(entry.BaseName),
                     RemoveProMarker(looseAlias),
                     looseAlias,
                     baseAlias
                 })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static string RemoveSpecificationPrefix(string? text, string? specificationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(specificationToken) &&
            text.StartsWith(specificationToken, StringComparison.OrdinalIgnoreCase))
        {
            return text[specificationToken.Length..].Trim();
        }

        return text.Trim();
    }

    private static string RemoveColorSuffix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text.Trim(), "(黑|灰|蓝|粉|棕|茶|绿|紫|金|银|白|红|橘|黄)$", string.Empty, RegexOptions.IgnoreCase).Trim();
    }

    private static string RemoveProMarker(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text, @"\s*pro\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
    }

    private static string RemoveDigits(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text, @"\d+", string.Empty).Trim();
    }

    private static List<string> BuildBaseSearchNames(OrderItemDraft item)
    {
        var names = new List<string>();
        foreach (var value in new[]
                 {
                     item.ProductCodeSearchKeyword,
                     item.ProductName,
                     CleanupSearchText(item.SourceText)
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var cleaned = value.Trim();
            names.Add(cleaned);
            names.Add(RemoveDigits(cleaned));
            names.Add(MatchTextHelper.RemoveTrailingDegree(cleaned));
        }

        return names
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ExtractDegrees(OrderItemDraft item)
    {
        var degreeSource = !string.IsNullOrWhiteSpace(item.DegreeText)
            ? item.DegreeText
            : !string.IsNullOrWhiteSpace(item.ProductCodeSearchKeyword)
                ? item.ProductCodeSearchKeyword
                : item.SourceText;
        return Regex.Matches(degreeSource ?? string.Empty, @"(?<!\d)(\d{1,4})(?!\d)")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CleanupSearchText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = text.Trim();
        cleaned = Regex.Replace(cleaned, @"^(款式|下单|商品|型号|品名|品牌)[:：]?\s*", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"^\d+\s*", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(一个|两个|三个|一副|两副|一盒|两盒|一片|两片)", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\d{1,4}\s*度", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[xX]\d+", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[\/，,].*$", string.Empty);
        return cleaned.Trim();
    }

    private static void AddCompactToken(ICollection<string> tokens, string? value)
    {
        var compact = MatchTextHelper.Compact(value);
        if (!string.IsNullOrWhiteSpace(compact))
        {
            foreach (var token in EnumerateCompactTokenVariants(compact))
            {
                tokens.Add(token);
            }
        }
    }

    private static IEnumerable<string> EnumerateCompactTokenVariants(string compact)
    {
        yield return compact;

        if (compact.Contains("梦镜", StringComparison.OrdinalIgnoreCase))
        {
            yield return compact.Replace("梦镜", "梦境", StringComparison.OrdinalIgnoreCase);
        }

        if (compact.Contains("梦境", StringComparison.OrdinalIgnoreCase))
        {
            yield return compact.Replace("梦境", "梦镜", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string ExtractInitials(string text)
    {
        var builder = new StringBuilder();
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '/' or '\\' or '|' or '，' or ',' or '。' or ':')
            {
                continue;
            }

            if ((ch >= 'a' && ch <= 'z') ||
                (ch >= 'A' && ch <= 'Z') ||
                (ch >= '0' && ch <= '9'))
            {
                builder.Append(char.ToLowerInvariant(ch));
                continue;
            }

            var initial = GetChineseInitial(ch);
            if (initial != '\0')
            {
                builder.Append(initial);
            }
        }

        return builder.ToString();
    }

    private static char GetChineseInitial(char ch)
    {
        try
        {
            var bytes = Encoding.GetEncoding("GB2312").GetBytes(ch.ToString());
            if (bytes.Length < 2)
            {
                return '\0';
            }

            var code = bytes[0] * 256 + bytes[1] - 65536;
            return code switch
            {
                >= -20319 and <= -20284 => 'a',
                >= -20283 and <= -19776 => 'b',
                >= -19775 and <= -19219 => 'c',
                >= -19218 and <= -18711 => 'd',
                >= -18710 and <= -18527 => 'e',
                >= -18526 and <= -18240 => 'f',
                >= -18239 and <= -17923 => 'g',
                >= -17922 and <= -17418 => 'h',
                >= -17417 and <= -16475 => 'j',
                >= -16474 and <= -16213 => 'k',
                >= -16212 and <= -15641 => 'l',
                >= -15640 and <= -15166 => 'm',
                >= -15165 and <= -14923 => 'n',
                >= -14922 and <= -14915 => 'o',
                >= -14914 and <= -14631 => 'p',
                >= -14630 and <= -14150 => 'q',
                >= -14149 and <= -14091 => 'r',
                >= -14090 and <= -13319 => 's',
                >= -13318 and <= -12839 => 't',
                >= -12838 and <= -12557 => 'w',
                >= -12556 and <= -11848 => 'x',
                >= -11847 and <= -11056 => 'y',
                >= -11055 and <= -10247 => 'z',
                _ => '\0'
            };
        }
        catch
        {
            return '\0';
        }
    }

    private static int ParseDegree(string value)
    {
        return int.TryParse(value, out var degree) ? degree : int.MaxValue;
    }

    private static ResolverContext BuildResolverContext(WorkflowSettingsSnapshot snapshot)
    {
        var catalog = snapshot.ProductCatalog
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ProductCode))
            .GroupBy(entry => entry.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var byCode = catalog.ToDictionary(entry => entry.ProductCode, StringComparer.OrdinalIgnoreCase);
        var byFamily = catalog
            .GroupBy(GetFamilyKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ProductCatalogEntry>)group.ToList(), StringComparer.OrdinalIgnoreCase);
        var byLooseFamily = catalog
            .GroupBy(GetLooseFamilyKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ProductCatalogEntry>)group.ToList(), StringComparer.OrdinalIgnoreCase);
        var productCodeAliases = snapshot.ProductCodeMappings
            .Where(row => !string.IsNullOrWhiteSpace(row.Alias) && !string.IsNullOrWhiteSpace(row.ProductCode))
            .Select(row => new ProductCodeAlias(row, MatchTextHelper.Compact(row.Alias)))
            .Where(alias => !string.IsNullOrWhiteSpace(alias.CompactAlias))
            .ToList();

        return new ResolverContext(
            catalog,
            byCode,
            byFamily,
            byLooseFamily,
            BuildDegreeOptions(catalog),
            productCodeAliases);
    }

    private static string Safe(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private sealed record MatchContext(
        IReadOnlyList<string> CompactTokens,
        string DegreeKey,
        string WearPeriod,
        int? PreferredDailyPackCount);

    private sealed record CatalogFamilyMatch(IReadOnlyList<ProductCatalogEntry> Entries, string MatchNote);

    private sealed record CatalogFamilyRank(IReadOnlyList<ProductCatalogEntry> Entries, int Score);

    private sealed record CatalogEntryMatch(
        ProductCatalogEntry Entry,
        bool FamilyMatched,
        bool WearMatched,
        bool DegreeMatched,
        int FieldMatchCount,
        int FamilyScore,
        int Score,
        string MatchNote);

    private sealed record ProductCodeAlias(ProductCodeMappingRow Row, string CompactAlias);

    private sealed class ResolverContext
    {
        public ResolverContext(
            IReadOnlyList<ProductCatalogEntry> catalog,
            IReadOnlyDictionary<string, ProductCatalogEntry> byCode,
            IReadOnlyDictionary<string, IReadOnlyList<ProductCatalogEntry>> byFamily,
            IReadOnlyDictionary<string, IReadOnlyList<ProductCatalogEntry>> byLooseFamily,
            IReadOnlyList<string> allDegreeOptions,
            IReadOnlyList<ProductCodeAlias> productCodeAliases)
        {
            Catalog = catalog;
            ByCode = byCode;
            ByFamily = byFamily;
            ByLooseFamily = byLooseFamily;
            AllDegreeOptions = allDegreeOptions;
            ProductCodeAliases = productCodeAliases;
        }

        public IReadOnlyList<ProductCatalogEntry> Catalog { get; }

        public IReadOnlyDictionary<string, ProductCatalogEntry> ByCode { get; }

        public IReadOnlyDictionary<string, IReadOnlyList<ProductCatalogEntry>> ByFamily { get; }

        public IReadOnlyDictionary<string, IReadOnlyList<ProductCatalogEntry>> ByLooseFamily { get; }

        public IReadOnlyList<string> AllDegreeOptions { get; }

        public IReadOnlyList<ProductCodeAlias> ProductCodeAliases { get; }

        public IReadOnlyList<ProductCatalogEntry> GetFamilyEntries(ProductCatalogEntry entry)
        {
            return ByFamily.TryGetValue(GetFamilyKey(entry), out var entries)
                ? entries
                : Array.Empty<ProductCatalogEntry>();
        }
    }
}
