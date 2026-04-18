using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OrderTextTrainer.Core.Services;

namespace WpfApp11;

public partial class ProductCodePickerWindow : Window
{
    private readonly OrderItemDraft _sourceItem;
    private readonly OrderItemDraft _workingCopy;
    private readonly WorkflowSettingsSnapshot _snapshot;

    public event EventHandler<ProductCodeSelectionConfirmedEventArgs>? Confirmed;

    public ProductCodeOption? SelectedOption { get; private set; }

    public string ConfirmedKeyword => _workingCopy.ProductCodeSearchKeyword;

    public ProductCodePickerWindow(OrderItemDraft item, WorkflowSettingsSnapshot snapshot)
    {
        InitializeComponent();

        _sourceItem = item;
        _snapshot = snapshot;
        _workingCopy = CloneItem(item);

        TxtSource.Text = item.SourceText;
        TxtKeyword.Text = item.ProductCodeSearchKeyword;
        RefreshWorkingCopy(TxtKeyword.Text);
        UpdateHeader();
        Loaded += (_, _) =>
        {
            TxtKeyword.Focus();
            SelectDefaultCandidate();
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
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
        RefreshWorkingCopy(TxtKeyword.Text);
    }

    private void TxtKeyword_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmSelection();
            e.Handled = true;
        }
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

    private void RefreshWorkingCopy(string? keyword)
    {
        _workingCopy.ProductCode = string.Empty;
        _workingCopy.ProductCodeSearchKeyword = keyword?.Trim() ?? string.Empty;

        var resolver = new CatalogSkuResolver();
        resolver.RefreshItem(_workingCopy, _snapshot);

        var filtered = ProductCodeSearchHelper.FilterOptions(_workingCopy.ProductCodeOptions, _workingCopy.ProductCodeSearchKeyword);
        ListCandidates.ItemsSource = filtered;
        TxtSummary.Text = filtered.Count == 0
            ? "没有找到候选，请继续输入周期、型号或度数。"
            : filtered.Count == 1
                ? $"找到 1 个候选：{filtered[0].DisplayText}"
                : $"找到 {filtered.Count} 个候选，请选中后确认。";

        SelectDefaultCandidate(filtered);

        UpdateHeader();
        UpdateConfirmHint();
    }

    private void SelectDefaultCandidate(IReadOnlyList<ProductCodeOption>? filteredOverride = null)
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
        if (!string.IsNullOrWhiteSpace(_sourceItem.ProductCode))
        {
            selectedOption = filtered.FirstOrDefault(option =>
                string.Equals(option.ProductCode, _sourceItem.ProductCode, StringComparison.OrdinalIgnoreCase));
        }

        if (selectedOption is null)
        {
            var knownWearPeriod = _workingCopy.WearPeriod?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(knownWearPeriod))
            {
                selectedOption = filtered.FirstOrDefault(option =>
                    IsExactOption(option) && WearPeriodMatches(option.WearPeriod, knownWearPeriod));
            }
        }

        if (selectedOption is null)
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
        TxtWearPeriod.Text = string.IsNullOrWhiteSpace(_workingCopy.WearPeriod) ? "周期: 未识别" : $"周期: {_workingCopy.WearPeriod}";
        TxtProductName.Text = string.IsNullOrWhiteSpace(_workingCopy.ProductName) ? "型号: 未识别" : $"型号: {_workingCopy.ProductName}";
        TxtDegree.Text = string.IsNullOrWhiteSpace(_workingCopy.DegreeText) ? "度数: 未识别" : $"度数: {_workingCopy.DegreeText}";
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
        BtnConfirm.Content = exact ? "一键确认" : "确认选择";
    }

    private void ConfirmSelection()
    {
        if (SelectedOption is null)
        {
            MessageBox.Show(this, "请先选择一个商品编码。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Confirmed?.Invoke(this, new ProductCodeSelectionConfirmedEventArgs(SelectedOption, ConfirmedKeyword));
        Close();
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
            ProductCodeConfirmed = item.ProductCodeConfirmed
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
