using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OrderTextTrainer.Core.Services;

namespace WpfApp11;

public partial class ProductCodePickerWindow : Window
{
    private const int SearchDebounceMilliseconds = 120;
    private readonly CatalogSkuResolver _resolver = new();

    private readonly OrderItemDraft _sourceItem;
    private readonly OrderItemDraft _workingCopy;
    private readonly WorkflowSettingsSnapshot _snapshot;
    private readonly List<ProductCodeOption> _allOptions;
    private readonly List<ProductCodeOption> _freeSearchOptions;
    private readonly DispatcherTimer _searchDebounceTimer;
    private bool _suppressKeywordRefresh;
    private bool _isFreeSearchMode;

    public event EventHandler<ProductCodeSelectionConfirmedEventArgs>? Confirmed;

    public ProductCodeOption? SelectedOption { get; private set; }

    public string ConfirmedKeyword => TxtKeyword.Text?.Trim() ?? string.Empty;

    public ProductCodePickerWindow(OrderItemDraft item, WorkflowSettingsSnapshot snapshot)
    {
        InitializeComponent();

        _sourceItem = item;
        _snapshot = snapshot;
        _workingCopy = CloneItem(item);
        _allOptions = LoadOptionCache();
        _freeSearchOptions = _resolver.BuildFreeSearchOptions(_snapshot, _workingCopy)
            .Select(CloneOption)
            .ToList();
        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SearchDebounceMilliseconds)
        };
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

        TxtSource.Text = item.SourceText;

        var initialKeyword = ResolveInitialKeyword(item);
        _suppressKeywordRefresh = true;
        TxtKeyword.Text = initialKeyword;
        _suppressKeywordRefresh = false;

        ApplyFilter(initialKeyword);
        UpdateHeader();
        Loaded += (_, _) =>
        {
            TxtKeyword.Focus();
            TxtKeyword.SelectAll();
            SelectDefaultCandidate();
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyPendingFilter();
            ConfirmSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void TxtKeyword_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressKeywordRefresh)
        {
            return;
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void TxtKeyword_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyPendingFilter();
            ConfirmSelection();
            e.Handled = true;
        }
    }

    private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        ApplyFilter(TxtKeyword.Text);
    }

    private void ListCandidates_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedOption = ListCandidates.SelectedItem as ProductCodeOption;
        UpdateConfirmHint();
    }

    private void ListCandidates_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ListCandidates.SelectedItem is ProductCodeOption option)
        {
            SelectedOption = option;
            UpdateConfirmHint();
            ConfirmSelection();
        }
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplyPendingFilter()
    {
        if (!_searchDebounceTimer.IsEnabled)
        {
            return;
        }

        _searchDebounceTimer.Stop();
        ApplyFilter(TxtKeyword.Text);
    }

    private void ApplyFilter(string? keyword)
    {
        _workingCopy.ProductCodeSearchKeyword = keyword?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_workingCopy.ProductCodeSearchKeyword))
        {
            _isFreeSearchMode = true;
        }

        var filtered = _isFreeSearchMode
            ? FilterFreeSearchOptions(_workingCopy.ProductCodeSearchKeyword)
            : ProductCodeSearchHelper.FilterOptions(_allOptions, _workingCopy.ProductCodeSearchKeyword);
        ListCandidates.ItemsSource = filtered.VisibleOptions;
        TxtSummary.Text = BuildSummaryText(filtered, _isFreeSearchMode);

        SelectDefaultCandidate(filtered.VisibleOptions, allowSmartDefault: !_isFreeSearchMode);
        UpdateConfirmHint();
    }

    private void SelectDefaultCandidate(IReadOnlyList<ProductCodeOption>? filteredOverride = null, bool allowSmartDefault = true)
    {
        var filtered = filteredOverride ?? ListCandidates.Items.OfType<ProductCodeOption>().ToList();
        if (filtered.Count == 0)
        {
            SelectedOption = null;
            ListCandidates.SelectedItem = null;
            UpdateConfirmHint();
            return;
        }

        ProductCodeOption? selectedOption = null;
        if (allowSmartDefault && !string.IsNullOrWhiteSpace(_sourceItem.ProductCode))
        {
            selectedOption = filtered.FirstOrDefault(option =>
                string.Equals(option.ProductCode, _sourceItem.ProductCode, StringComparison.OrdinalIgnoreCase));
        }

        if (allowSmartDefault && selectedOption is null)
        {
            var knownWearPeriod = _workingCopy.WearPeriod?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(knownWearPeriod))
            {
                selectedOption = filtered.FirstOrDefault(option =>
                    IsExactOption(option) && WearPeriodMatches(option.WearPeriod, knownWearPeriod));
            }
        }

        if (allowSmartDefault && selectedOption is null)
        {
            selectedOption = filtered.FirstOrDefault(IsExactOption);
        }

        if (selectedOption is null && SelectedOption is not null)
        {
            selectedOption = filtered.FirstOrDefault(option =>
                string.Equals(option.ProductCode, SelectedOption.ProductCode, StringComparison.OrdinalIgnoreCase));
        }

        if (selectedOption is null)
        {
            selectedOption = filtered[0];
        }

        ListCandidates.SelectedItem = selectedOption;
        ListCandidates.ScrollIntoView(selectedOption);
        UpdateConfirmHint();
    }

    private static bool IsExactOption(ProductCodeOption option)
    {
        return string.Equals(option.MatchState, "Exact", StringComparison.OrdinalIgnoreCase);
    }

    private static bool WearPeriodMatches(string optionWearPeriod, string knownWearPeriod)
    {
        var left = MatchTextHelper.Compact(optionWearPeriod);
        var right = MatchTextHelper.Compact(knownWearPeriod);
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
               right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateHeader()
    {
        TxtWearPeriod.Text = string.IsNullOrWhiteSpace(_workingCopy.WearPeriod)
            ? "周期: 未识别"
            : $"周期: {_workingCopy.WearPeriod}";
        TxtProductName.Text = string.IsNullOrWhiteSpace(_workingCopy.ProductName)
            ? "型号: 未识别"
            : $"型号: {_workingCopy.ProductName}";
        TxtDegree.Text = string.IsNullOrWhiteSpace(_workingCopy.DegreeText)
            ? "度数: 未识别"
            : $"度数: {_workingCopy.DegreeText}";
        TxtMatchState.Text = _workingCopy.ProductMatchStatusText;

        BorderMatchState.Background = _workingCopy.ProductMatchState switch
        {
            "Exact" => System.Windows.Media.Brushes.Honeydew,
            "Partial" => System.Windows.Media.Brushes.Khaki,
            _ => System.Windows.Media.Brushes.Gainsboro
        };
        TxtMatchState.Foreground = _workingCopy.ProductMatchState switch
        {
            "Exact" => System.Windows.Media.Brushes.ForestGreen,
            "Partial" => System.Windows.Media.Brushes.Goldenrod,
            _ => System.Windows.Media.Brushes.DimGray
        };
    }

    private void UpdateConfirmHint()
    {
        if (SelectedOption is null)
        {
            TxtConfirmHint.Text = "请选择一个候选商品编码后再确认。";
            BtnConfirm.Content = "确认选择";
            return;
        }

        var exact = string.Equals(SelectedOption.MatchState, "Exact", StringComparison.OrdinalIgnoreCase);
        TxtConfirmHint.Text = exact
            ? $"当前候选已完全匹配：{SelectedOption.ProductCode}，可一键确认。"
            : $"已选中：{SelectedOption.ProductCode}，确认后关闭。";
        BtnConfirm.Content = "确认选择";
    }

    private void ConfirmSelection()
    {
        ApplyPendingFilter();

        if (SelectedOption is null)
        {
            MessageBox.Show(
                this,
                "请先选择一个商品编码。",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Confirmed?.Invoke(this, new ProductCodeSelectionConfirmedEventArgs(SelectedOption, ConfirmedKeyword));
        Close();
    }

    private ProductCodeFilterResult FilterFreeSearchOptions(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return ProductCodeSearchHelper.FilterOptions(_freeSearchOptions, keyword);
        }

        var rankedOptions = _resolver.BuildFreeSearchOptionsByKeyword(_snapshot, keyword, _workingCopy)
            .Select(CloneOption)
            .ToList();
        var visible = rankedOptions
            .Take(ProductCodeSearchHelper.MaxVisibleCount)
            .ToList();
        return new ProductCodeFilterResult(visible, rankedOptions.Count, rankedOptions.Count > visible.Count);
    }

    private List<ProductCodeOption> LoadOptionCache()
    {
        if (_workingCopy.ProductCodeOptions.Count > 0)
        {
            return _workingCopy.ProductCodeOptions
                .Select(CloneOption)
                .ToList();
        }

        _resolver.RefreshItem(_workingCopy, _snapshot);
        return _workingCopy.ProductCodeOptions
            .Select(CloneOption)
            .ToList();
    }

    private static string ResolveInitialKeyword(OrderItemDraft item)
    {
        var currentKeyword = item.ProductCodeSearchKeyword?.Trim() ?? string.Empty;
        var keywordEqualsCode = !string.IsNullOrWhiteSpace(currentKeyword) &&
                                !string.IsNullOrWhiteSpace(item.ProductCode) &&
                                string.Equals(currentKeyword, item.ProductCode, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(currentKeyword) && !(item.ProductCodeConfirmed && keywordEqualsCode))
        {
            return currentKeyword;
        }

        var recognizedKeyword = string.Join(" ", new[]
            {
                item.WearPeriod?.Trim(),
                item.ProductName?.Trim(),
                item.DegreeText?.Trim()
            }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        if (!string.IsNullOrWhiteSpace(recognizedKeyword))
        {
            return recognizedKeyword;
        }

        return currentKeyword;
    }

    private static string BuildSummaryText(ProductCodeFilterResult filtered, bool isFreeSearchMode)
    {
        if (filtered.TotalMatches == 0)
        {
            return isFreeSearchMode
                ? "已切换至自由搜索，请输入商品编码、型号、周期或度数关键词。"
                : "没有找到候选，请继续输入周期、型号或度数。";
        }

        if (filtered.TotalMatches == 1)
        {
            return isFreeSearchMode
                ? $"自由搜索找到 1 个候选：{filtered.VisibleOptions[0].DisplayText}"
                : $"找到 1 个候选：{filtered.VisibleOptions[0].DisplayText}";
        }

        if (filtered.IsTruncated)
        {
            return isFreeSearchMode
                ? $"已进入自由搜索，找到 {filtered.TotalMatches} 个候选，当前只显示前 {filtered.VisibleOptions.Count} 个，请继续输入关键词缩小范围。"
                : $"找到 {filtered.TotalMatches} 个候选，为减少卡顿当前只显示前 {filtered.VisibleOptions.Count} 个，请继续输入关键词缩小范围。";
        }

        return isFreeSearchMode
            ? $"已进入自由搜索，找到 {filtered.TotalMatches} 个候选，请选中后确认。"
            : $"找到 {filtered.TotalMatches} 个候选，请选中后确认。";
    }

    private static OrderItemDraft CloneItem(OrderItemDraft item)
    {
        return new OrderItemDraft
        {
            SourceText = item.SourceText,
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            SpecCodeText = item.SpecCodeText,
            BarcodeText = item.BarcodeText,
            WearPeriod = item.WearPeriod,
            QuantityText = item.QuantityText,
            Remark = item.Remark,
            DegreeText = item.DegreeText,
            ProductCodeSearchKeyword = item.ProductCodeSearchKeyword,
            ProductCodeSearchSummary = item.ProductCodeSearchSummary,
            IsTrial = item.IsTrial,
            MatchHint = item.MatchHint,
            ProductMatchState = item.ProductMatchState,
            ProductMatchStatusText = item.ProductMatchStatusText,
            ProductWorkflowStage = item.ProductWorkflowStage,
            ProductWorkflowDetail = item.ProductWorkflowDetail,
            ProductCodeConfirmed = item.ProductCodeConfirmed,
            UseManualProductCodeStyle = item.UseManualProductCodeStyle,
            ProductCodeOptions = item.ProductCodeOptions
                .Select(CloneOption)
                .ToList()
        };
    }

    private static ProductCodeOption CloneOption(ProductCodeOption option)
    {
        return new ProductCodeOption
        {
            ProductCode = option.ProductCode,
            CoreCode = option.CoreCode,
            WearPeriod = option.WearPeriod,
            ModelName = option.ModelName,
            DegreeText = option.DegreeText,
            DisplayText = option.DisplayText,
            SearchText = option.SearchText,
            Initials = option.Initials,
            SortOrder = option.SortOrder,
            MatchScore = option.MatchScore,
            MatchFieldCount = option.MatchFieldCount,
            MatchState = option.MatchState,
            MatchStateText = option.MatchStateText
        };
    }
}

public sealed class ProductCodeSelectionConfirmedEventArgs : EventArgs
{
    public ProductCodeSelectionConfirmedEventArgs(ProductCodeOption selectedOption, string confirmedKeyword)
    {
        SelectedOption = selectedOption;
        ConfirmedKeyword = confirmedKeyword;
    }

    public ProductCodeOption SelectedOption { get; }

    public string ConfirmedKeyword { get; }
}
