using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using OrderTextTrainer.Core.Models;
using OrderTextTrainer.Core.Services;
using System.Text.RegularExpressions;

namespace WpfApp11;

public partial class MainWindow : Window
{
    private readonly WorkflowSettingsRepository _settingsRepository = new();
    private readonly ProductCatalogRepository _productCatalogRepository = new();
    private readonly OrderHistoryRepository _historyRepository = new();
    private readonly OrderAuditRepository _auditRepository = new();
    private readonly OrderDraftFactory _draftFactory = new();
    private readonly OrderDraftValidator _draftValidator = new();
    private readonly CatalogSkuResolver _catalogSkuResolver = new();
    private readonly HupunB2cTradeUploader _tradeUploader = new();

    private ParserRuleSet _ruleSet = ParserRuleSet.CreateDefault();
    private ObservableCollection<LookupValueRow> _wearPeriods = new();
    private ObservableCollection<WearPeriodMappingRow> _wearMappings = new();
    private ObservableCollection<ProductCatalogEntry> _productCatalog = new();
    private ObservableCollection<ProductCodeMappingRow> _productMappings = new();
    private ObservableCollection<UserAccountRow> _userAccounts = new();
    private ObservableCollection<OrderDraft> _draftOrders = new();
    private ObservableCollection<OrderAuditRecord> _historyEntries = new();
    private ParseResult? _lastParseResult;
    private UploadConfiguration _uploadConfiguration = new();
    private OrderDraft? _selectedDraft;

    public MainWindow()
    {
        InitializeComponent();
        LoadSettingsIntoUi(_settingsRepository.LoadOrCreate());
        LoadHistory();

        TxtInput.Text = SampleData.DefaultText;
        TxtParseSummary.Text = "把一段或多段订单文本贴到左侧，系统会拆成多条订单草稿。";
        TxtQueueSummary.Text = "当前还没有解析结果。";
        TxtValidationOutput.Text = "待校验。";
        TxtUploadOutput.Text = "待上传。";
        TxtCurrentRawOrder.Text = "尚未选择订单。";
        TxtCurrentDraftHeadline.Text = "尚未选择订单";
        TxtCurrentDraftMeta.Text = "先解析文本，再从队列中选择一条订单开始审核。";
        TxtDraftEditorSummary.Text = "未选中订单。右侧会显示当前订单的收件信息、商品和校验状态。";
        TxtWorkbenchSummary.Text = "当前批次还没有订单草稿。";
        TxtFlowHint.Text = "先在左侧粘贴订单文本，点击“解析文本”生成草稿。";
        TxtHistoryRaw.Text = "请选择一条历史记录。";
        TxtHistorySnapshot.Text = "请选择一条历史记录。";
        TxtHistoryResponse.Text = "请选择一条历史记录。";
        TxtStatus.Text = "准备就绪。";
        TxtSettingsStatus.Text = "设置已加载。";
        UpdateWorkbenchState();
    }

    private void BtnParse_Click(object sender, RoutedEventArgs e)
    {
        PersistSelectedDraftFromForm();
        var snapshot = BuildSnapshotFromUi();
        var selectedAccount = CmbOperatorAccounts.SelectedItem as UserAccountRow;
        var drafts = _draftFactory.CreateDrafts(TxtInput.Text, snapshot, selectedAccount, out var parseResult);

        _lastParseResult = parseResult;
        _draftOrders = new ObservableCollection<OrderDraft>(drafts);
        AssignDraftOrderNumbers();
        RefreshAllDraftResolutions();
        GridDraftOrders.ItemsSource = _draftOrders;

        TxtParseSummary.Text =
            $"识别 {parseResult.Orders.Count} 个订单，未识别片段 {parseResult.UnknownSegments.Count} 条，警告 {parseResult.Warnings.Count} 条。";
        TxtQueueSummary.Text = _draftOrders.Count == 0
            ? "没有生成订单草稿。"
            : $"已生成 {_draftOrders.Count} 条订单草稿。上传成功后会自动切到下一条。";
        TxtValidationOutput.Text = parseResult.Warnings.Count == 0
            ? "解析完成，等待审核。"
            : string.Join(Environment.NewLine, parseResult.Warnings);
        TxtUploadOutput.Text = "待上传。";

        if (_draftOrders.Count > 0)
        {
            GridDraftOrders.SelectedIndex = 0;
        }
        else
        {
            _selectedDraft = null;
            LoadDraftToForm(null);
        }

        foreach (var draft in _draftOrders)
        {
            SaveHistoryEntry(draft, "解析完成，生成订单草稿。", "解析生成");
        }

        TxtStatus.Text = $"解析完成，共 {_draftOrders.Count} 条订单草稿。";
        UpdateWorkbenchState();
    }

    private void BtnSaveParseRecord_Click(object sender, RoutedEventArgs e)
    {
        PersistSelectedDraftFromForm();
        if (_draftOrders.Count == 0)
        {
            MessageBox.Show("当前没有可保存的解析记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var draft in _draftOrders)
        {
            SaveHistoryEntry(draft, draft.StatusDetail, "保存录入");
        }

        LoadHistory();
        TxtStatus.Text = $"已保存 {_draftOrders.Count} 条解析记录。";
        UpdateWorkbenchState();
    }

    private void BtnValidateCurrent_Click(object sender, RoutedEventArgs e)
    {
        PersistSelectedDraftFromForm();
        if (_selectedDraft is null)
        {
            MessageBox.Show("请先选择一条订单草稿。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ValidateDraft(_selectedDraft);
        RefreshDraftViews();
    }

    private async void BtnSubmitCurrent_Click(object sender, RoutedEventArgs e)
    {
        PersistSelectedDraftFromForm();
        if (_selectedDraft is null)
        {
            MessageBox.Show("请先选择一条订单草稿。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await UploadDraftAsync(_selectedDraft, moveToNext: true);
    }

    private async void BtnSubmitAll_Click(object sender, RoutedEventArgs e)
    {
        PersistSelectedDraftFromForm();
        if (_draftOrders.Count == 0)
        {
            MessageBox.Show("当前没有待上传订单。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnSubmitAll.IsEnabled = false;
        try
        {
            foreach (var draft in _draftOrders.ToList())
            {
                GridDraftOrders.SelectedItem = draft;
                await UploadDraftAsync(draft, moveToNext: false);
            }

            MoveToNextDraft();
        }
        finally
        {
            UpdateActionAvailability();
        }
    }

    private void BtnSkipCurrent_Click(object sender, RoutedEventArgs e)
    {
        PersistSelectedDraftFromForm();
        if (_selectedDraft is null)
        {
            return;
        }

        _selectedDraft.Status = "已跳过";
        _selectedDraft.StatusDetail = "人工跳过。";
        SaveHistoryEntry(_selectedDraft, _selectedDraft.StatusDetail, "跳过订单");
        RefreshDraftViews();
        MoveToNextDraft();
    }

    private void BtnLoadSample_Click(object sender, RoutedEventArgs e)
    {
        TxtInput.Text = SampleData.DefaultText;
        TxtStatus.Text = "已加载示例文本。";
        UpdateWorkbenchState();
    }

    private void BtnImportCatalog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入商品列表",
            Filter = "Excel 商品表 (*.xlsx)|*.xlsx|JSON 商品表 (*.json)|*.json|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            IReadOnlyList<ProductCatalogEntry> entries;
            if (Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                entries = _productCatalogRepository.LoadOrCreateCatalog(dialog.FileName);
            }
            else
            {
                entries = _productCatalogRepository.ImportFromXlsx(dialog.FileName);
            }

            _productCatalog = new ObservableCollection<ProductCatalogEntry>(entries);
            GridProductCatalog.ItemsSource = _productCatalog;
            var snapshot = BuildSnapshotFromUi();
            _settingsRepository.Save(snapshot);
            RefreshLookupSources();
            RefreshAllDraftResolutions();
            TxtStatus.Text = $"已导入商品列表，共 {_productCatalog.Count} 条。";
            TxtSettingsStatus.Text = "商品列表已导入并保存到本地。";
            UpdateWorkbenchState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入商品列表失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnAddItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDraft is null)
        {
            return;
        }

        _selectedDraft.Items.Add(new OrderItemDraft
        {
            QuantityText = "1",
            Remark = string.Empty,
            MatchHint = "手工新增商品。"
        });

        GridDraftItems.Items.Refresh();
        GridDraftOrders.Items.Refresh();
        UpdateWorkbenchState();
    }

    private void BtnRemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDraft is null || GridDraftItems.SelectedItem is not OrderItemDraft selectedItem)
        {
            return;
        }

        _selectedDraft.Items.Remove(selectedItem);
        GridDraftItems.Items.Refresh();
        GridDraftOrders.Items.Refresh();
        UpdateWorkbenchState();
    }

    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        PersistSelectedDraftFromForm();
        var snapshot = BuildSnapshotFromUi();
        _settingsRepository.Save(snapshot);
        TxtSettingsStatus.Text = "设置已保存到运行目录。";
        TxtStatus.Text = "设置保存完成。";
        LoadSettingsIntoUi(snapshot);
        UpdateWorkbenchState();
    }

    private void BtnImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入设置快照",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var snapshot = _settingsRepository.Import(dialog.FileName);
            LoadSettingsIntoUi(snapshot);
            TxtSettingsStatus.Text = $"已导入设置：{dialog.FileName}";
            UpdateWorkbenchState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入设置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出设置快照",
            Filter = "JSON 文件 (*.json)|*.json",
            FileName = $"workflow-settings-{DateTime.Now:yyyyMMddHHmmss}.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var snapshot = BuildSnapshotFromUi();
        _settingsRepository.Export(snapshot, dialog.FileName);
        TxtSettingsStatus.Text = $"已导出设置：{dialog.FileName}";
        UpdateWorkbenchState();
    }

    private void BtnAddSettingsRow_Click(object sender, RoutedEventArgs e)
    {
        switch (SettingsTabs.SelectedIndex)
        {
            case 0:
                _wearPeriods.Add(new LookupValueRow { Value = string.Empty });
                break;
            case 1:
                _wearMappings.Add(new WearPeriodMappingRow());
                break;
            case 2:
                _productCatalog.Add(new ProductCatalogEntry());
                break;
            case 3:
                _productMappings.Add(new ProductCodeMappingRow());
                break;
            case 4:
                _userAccounts.Add(new UserAccountRow());
                break;
            default:
                TxtSettingsStatus.Text = "接口配置页直接编辑文本框即可。";
                return;
        }

        RefreshLookupSources();
        TxtSettingsStatus.Text = "已新增一行。";
        UpdateWorkbenchState();
    }

    private void BtnRemoveSettingsRow_Click(object sender, RoutedEventArgs e)
    {
        switch (SettingsTabs.SelectedIndex)
        {
            case 0 when GridWearPeriods.SelectedItem is LookupValueRow wearPeriod:
                _wearPeriods.Remove(wearPeriod);
                break;
            case 1 when GridWearMappings.SelectedItem is WearPeriodMappingRow wearMapping:
                _wearMappings.Remove(wearMapping);
                break;
            case 2 when GridProductCatalog.SelectedItem is ProductCatalogEntry product:
                _productCatalog.Remove(product);
                break;
            case 3 when GridProductMappings.SelectedItem is ProductCodeMappingRow mapping:
                _productMappings.Remove(mapping);
                break;
            case 4 when GridUserAccounts.SelectedItem is UserAccountRow userAccount:
                _userAccounts.Remove(userAccount);
                break;
            default:
                TxtSettingsStatus.Text = "请选择要删除的行。";
                return;
        }

        RefreshLookupSources();
        TxtSettingsStatus.Text = "已删除选中行。";
        UpdateWorkbenchState();
    }

    private void GridDraftOrders_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(GridDraftOrders.SelectedItem, _selectedDraft))
        {
            return;
        }

        PersistSelectedDraftFromForm();
        _selectedDraft = GridDraftOrders.SelectedItem as OrderDraft;
        LoadDraftToForm(_selectedDraft);
    }

    private void CmbOperatorAccounts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbOperatorAccounts.SelectedItem is not UserAccountRow selectedAccount)
        {
            return;
        }

        if (_selectedDraft is not null)
        {
            _selectedDraft.OperatorLoginName = selectedAccount.LoginName;
            _selectedDraft.OperatorErpId = selectedAccount.ErpId;
            AssignDraftOrderNumbers();
            CmbDraftOperator.SelectedItem = FindUserAccount(selectedAccount.LoginName);
            GridDraftOrders.Items.Refresh();
        }

        TxtStatus.Text = $"当前账号：{selectedAccount}";
        UpdateWorkbenchState();
    }

    private void CmbDraftOperator_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedDraft is null || CmbDraftOperator.SelectedItem is not UserAccountRow selectedAccount)
        {
            return;
        }

        _selectedDraft.OperatorLoginName = selectedAccount.LoginName;
        _selectedDraft.OperatorErpId = selectedAccount.ErpId;
        AssignDraftOrderNumbers();
        GridDraftOrders.Items.Refresh();
        UpdateWorkbenchState();
    }

    private void GridDraftItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateActionAvailability();
    }

    private void GridDraftItems_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_selectedDraft is null)
            {
                return;
            }

            RefreshDraftResolution(_selectedDraft);
            GridDraftItems.Items.Refresh();
            GridDraftOrders.Items.Refresh();
            UpdateWorkbenchState();
        }), DispatcherPriority.Background);
    }

    private void ProductCodeComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is TextBox editor)
            {
                editor.TextChanged -= ProductCodeComboEditor_TextChanged;
                editor.TextChanged += ProductCodeComboEditor_TextChanged;
            }

            ApplyProductCodeComboFilter(comboBox, comboBox.Text);
        }
    }

    private void ProductCodeComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            ApplyProductCodeComboFilter(comboBox, comboBox.Text);
        }
    }

    private void ProductCodeComboBox_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter or Key.Tab or Key.Escape)
        {
            return;
        }

        ApplyProductCodeComboFilter(comboBox, comboBox.Text);
        comboBox.IsDropDownOpen = true;
    }

    private void ProductCodeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || _selectedDraft is null || comboBox.SelectedItem is not ProductCodeOption)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            ApplyProductCodeComboFilter(comboBox, string.Empty);
            RefreshDraftResolution(_selectedDraft);
            GridDraftItems.Items.Refresh();
            GridDraftOrders.Items.Refresh();
            UpdateWorkbenchState();
        }), DispatcherPriority.Background);
    }

    private void ProductCodeComboBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            ApplyProductCodeComboFilter(comboBox, string.Empty);
        }
    }

    private void ProductCodeComboEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox editor)
        {
            return;
        }

        var comboBox = FindVisualParent<ComboBox>(editor);
        if (comboBox is null)
        {
            return;
        }

        ApplyProductCodeComboFilter(comboBox, editor.Text);
        comboBox.IsDropDownOpen = true;
    }

    private void GridHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridHistory.SelectedItem is not OrderAuditRecord entry)
        {
            TxtHistoryRaw.Text = "请选择一条历史记录。";
            TxtHistorySnapshot.Text = "请选择一条历史记录。";
            TxtHistoryResponse.Text = "请选择一条历史记录。";
            return;
        }

        TxtHistoryRaw.Text = entry.RawText;
        TxtHistorySnapshot.Text = entry.SnapshotJson;
        TxtHistoryResponse.Text = entry.ResponseText;
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AppContext.BaseDirectory,
            UseShellExecute = true
        });
    }

    private void PersistSelectedDraftFromForm()
    {
        if (_selectedDraft is null)
        {
            return;
        }

        _selectedDraft.ReceiverName = TxtDraftReceiverName.Text.Trim();
        _selectedDraft.ReceiverMobile = TxtDraftReceiverMobile.Text.Trim();
        _selectedDraft.ReceiverAddress = TxtDraftReceiverAddress.Text.Trim();
        _selectedDraft.Remark = TxtDraftRemark.Text.Trim();
        _selectedDraft.HasGift = ChkDraftHasGift.IsChecked == true;

        if (CmbDraftOperator.SelectedItem is UserAccountRow selectedAccount)
        {
            _selectedDraft.OperatorLoginName = selectedAccount.LoginName;
            _selectedDraft.OperatorErpId = selectedAccount.ErpId;
        }

        AssignDraftOrderNumbers();
        RefreshDraftResolution(_selectedDraft);
        GridDraftOrders.Items.Refresh();
        UpdateSelectedDraftSummary(_selectedDraft);
    }

    private void LoadDraftToForm(OrderDraft? draft)
    {
        if (draft is null)
        {
            TxtDraftReceiverName.Text = string.Empty;
            TxtDraftReceiverMobile.Text = string.Empty;
            TxtDraftReceiverAddress.Text = string.Empty;
            TxtDraftRemark.Text = string.Empty;
            ChkDraftHasGift.IsChecked = false;
            CmbDraftOperator.SelectedItem = null;
            GridDraftItems.ItemsSource = null;
            TxtCurrentRawOrder.Text = "尚未选择订单。";
            UpdateSelectedDraftSummary(null);
            UpdateActionAvailability();
            return;
        }

        TxtDraftReceiverName.Text = draft.ReceiverName;
        TxtDraftReceiverMobile.Text = draft.ReceiverMobile;
        TxtDraftReceiverAddress.Text = draft.ReceiverAddress;
        TxtDraftRemark.Text = draft.Remark;
        ChkDraftHasGift.IsChecked = draft.HasGift;
        CmbDraftOperator.SelectedItem = FindUserAccount(draft.OperatorLoginName) ?? CmbOperatorAccounts.SelectedItem;
        GridDraftItems.ItemsSource = draft.Items;
        TxtCurrentRawOrder.Text = draft.RawText;
        TxtValidationOutput.Text = string.IsNullOrWhiteSpace(draft.StatusDetail)
            ? "待校验。"
            : draft.StatusDetail;
        UpdateSelectedDraftSummary(draft);
        UpdateActionAvailability();
    }

    private void LoadSettingsIntoUi(WorkflowSettingsSnapshot snapshot)
    {
        _ruleSet = snapshot.RuleSet ?? ParserRuleSet.CreateDefault();
        _wearPeriods = new ObservableCollection<LookupValueRow>(
            snapshot.WearPeriods
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .Select(item => new LookupValueRow { Value = item.Value.Trim() }));
        _wearMappings = new ObservableCollection<WearPeriodMappingRow>(
            snapshot.WearPeriodMappings.Select(item => new WearPeriodMappingRow
            {
                Alias = item.Alias,
                WearPeriod = item.WearPeriod
            }));
        _productCatalog = new ObservableCollection<ProductCatalogEntry>(
            snapshot.ProductCatalog.Select(item => new ProductCatalogEntry
            {
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                SpecCode = item.SpecCode,
                Barcode = item.Barcode,
                BaseName = item.BaseName,
                SpecificationToken = item.SpecificationToken,
                ModelToken = item.ModelToken,
                Degree = item.Degree,
                SearchText = item.SearchText
            }));
        _productMappings = new ObservableCollection<ProductCodeMappingRow>(
            snapshot.ProductCodeMappings.Select(item => new ProductCodeMappingRow
            {
                Alias = item.Alias,
                ProductCode = item.ProductCode,
                Note = item.Note
            }));
        _userAccounts = new ObservableCollection<UserAccountRow>(
            snapshot.UserAccounts.Select(item => new UserAccountRow
            {
                LoginName = item.LoginName,
                DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.LoginName : item.DisplayName,
                ErpId = item.ErpId
            }));
        _uploadConfiguration = snapshot.Upload ?? new UploadConfiguration();

        GridWearPeriods.ItemsSource = _wearPeriods;
        GridWearMappings.ItemsSource = _wearMappings;
        GridProductCatalog.ItemsSource = _productCatalog;
        GridProductMappings.ItemsSource = _productMappings;
        GridUserAccounts.ItemsSource = _userAccounts;
        GridDraftOrders.ItemsSource = _draftOrders;
        GridHistory.ItemsSource = _historyEntries;

        TxtUploadApiUrl.Text = _uploadConfiguration.ApiUrl;
        TxtUploadAppKey.Text = _uploadConfiguration.AppKey;
        TxtUploadSecret.Text = _uploadConfiguration.Secret;
        TxtOperatorFieldName.Text = _uploadConfiguration.OperatorErpFieldName;
        TxtGiftFieldName.Text = _uploadConfiguration.GiftFieldName;
        TxtItemWearPeriodFieldName.Text = _uploadConfiguration.ItemWearPeriodFieldName;

        RefreshLookupSources();
        RefreshAllDraftResolutions();
        if (_userAccounts.Count > 0 && CmbOperatorAccounts.SelectedItem is null)
        {
            CmbOperatorAccounts.SelectedIndex = 0;
        }

        UpdateWorkbenchState();
    }

    private void LoadHistory()
    {
        _historyEntries = new ObservableCollection<OrderAuditRecord>(_auditRepository.LoadOrCreate());
        GridHistory.ItemsSource = _historyEntries;
    }

    private void RefreshLookupSources()
    {
        ColSettingsWearMapping.ItemsSource = _wearPeriods.ToList();

        var previouslySelectedLogin = (CmbOperatorAccounts.SelectedItem as UserAccountRow)?.LoginName;
        CmbOperatorAccounts.ItemsSource = _userAccounts;
        CmbDraftOperator.ItemsSource = _userAccounts;

        if (!string.IsNullOrWhiteSpace(previouslySelectedLogin))
        {
            CmbOperatorAccounts.SelectedItem = FindUserAccount(previouslySelectedLogin);
        }
        else if (_userAccounts.Count > 0 && CmbOperatorAccounts.SelectedItem is null)
        {
            CmbOperatorAccounts.SelectedIndex = 0;
        }

        GridDraftItems.Items.Refresh();
    }

    private WorkflowSettingsSnapshot BuildSnapshotFromUi()
    {
        _uploadConfiguration = new UploadConfiguration
        {
            ApiUrl = TxtUploadApiUrl.Text.Trim(),
            AppKey = TxtUploadAppKey.Text.Trim(),
            Secret = TxtUploadSecret.Text.Trim(),
            OperatorErpFieldName = TxtOperatorFieldName.Text.Trim(),
            GiftFieldName = TxtGiftFieldName.Text.Trim(),
            ItemWearPeriodFieldName = TxtItemWearPeriodFieldName.Text.Trim()
        };

        return new WorkflowSettingsSnapshot
        {
            RuleSet = _ruleSet,
            WearPeriods = _wearPeriods
                .Select(item => new LookupValueRow { Value = Safe(item.Value) })
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .DistinctBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            WearPeriodMappings = _wearMappings
                .Select(item => new WearPeriodMappingRow
                {
                    Alias = Safe(item.Alias),
                    WearPeriod = Safe(item.WearPeriod)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Alias) && !string.IsNullOrWhiteSpace(item.WearPeriod))
                .DistinctBy(item => $"{item.WearPeriod}|{item.Alias}", StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ProductCatalog = _productCatalog
                .Select(item => new ProductCatalogEntry
                {
                    ProductCode = Safe(item.ProductCode),
                    ProductName = Safe(item.ProductName),
                    SpecCode = Safe(item.SpecCode),
                    Barcode = Safe(item.Barcode),
                    BaseName = Safe(item.BaseName),
                    SpecificationToken = Safe(item.SpecificationToken),
                    ModelToken = Safe(item.ModelToken),
                    Degree = Safe(item.Degree),
                    SearchText = Safe(item.SearchText)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode) || !string.IsNullOrWhiteSpace(item.ProductName))
                .ToList(),
            ProductCodeMappings = _productMappings
                .Select(item => new ProductCodeMappingRow
                {
                    Alias = Safe(item.Alias),
                    ProductCode = Safe(item.ProductCode),
                    Note = Safe(item.Note)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Alias))
                .ToList(),
            UserAccounts = _userAccounts
                .Select(item => new UserAccountRow
                {
                    LoginName = Safe(item.LoginName),
                    DisplayName = Safe(item.DisplayName),
                    ErpId = Safe(item.ErpId)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.LoginName))
                .ToList(),
            Upload = _uploadConfiguration
        };
    }

    private void ValidateDraft(OrderDraft draft)
    {
        var snapshot = BuildSnapshotFromUi();
        _catalogSkuResolver.RefreshDraft(draft, snapshot);
        var validation = _draftValidator.Validate(draft, snapshot);
        draft.Status = validation.IsValid ? "待上传" : "待补全";
        draft.StatusDetail = validation.ToString();
        TxtValidationOutput.Text = validation.ToString();
        SaveHistoryEntry(draft, draft.StatusDetail, "校验订单");
    }

    private async Task UploadDraftAsync(OrderDraft draft, bool moveToNext)
    {
        var snapshot = BuildSnapshotFromUi();
        _catalogSkuResolver.RefreshDraft(draft, snapshot);
        var validation = _draftValidator.Validate(draft, snapshot);
        if (!validation.IsValid)
        {
            draft.Status = "待补全";
            draft.StatusDetail = validation.ToString();
            TxtValidationOutput.Text = draft.StatusDetail;
            TxtUploadOutput.Text = "未上传，先修正必填项。";
            SaveHistoryEntry(draft, draft.StatusDetail, "校验未通过");
            RefreshDraftViews();
            if (moveToNext)
            {
                MoveToNextDraft();
            }

            return;
        }

        BtnSubmitCurrent.IsEnabled = false;
        TxtStatus.Text = $"正在上传订单 {DisplayValue(draft.OrderNumber, draft.DraftId)} ...";

        try
        {
            var result = await _tradeUploader.UploadAsync(draft, snapshot.Upload);
            draft.Status = result.IsSuccess ? "上传成功" : "上传失败";
            draft.StatusDetail = result.DebugText;
            TxtValidationOutput.Text = validation.ToString();
            TxtUploadOutput.Text = result.DebugText;
            SaveHistoryEntry(draft, result.ResponseText, result.IsSuccess ? "上传成功" : "上传失败");
        }
        catch (Exception ex)
        {
            draft.Status = "上传失败";
            draft.StatusDetail = ex.ToString();
            TxtUploadOutput.Text = ex.ToString();
            SaveHistoryEntry(draft, ex.ToString(), "上传异常");
        }
        finally
        {
            RefreshDraftViews();
            if (moveToNext)
            {
                MoveToNextDraft();
            }

            UpdateActionAvailability();
        }
    }

    private void MoveToNextDraft()
    {
        if (_draftOrders.Count == 0)
        {
            return;
        }

        var currentIndex = _selectedDraft is null ? -1 : _draftOrders.IndexOf(_selectedDraft);
        for (var index = currentIndex + 1; index < _draftOrders.Count; index++)
        {
            if (!string.Equals(_draftOrders[index].Status, "上传成功", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_draftOrders[index].Status, "已跳过", StringComparison.OrdinalIgnoreCase))
            {
                GridDraftOrders.SelectedItem = _draftOrders[index];
                GridDraftOrders.ScrollIntoView(_draftOrders[index]);
                return;
            }
        }

        TxtStatus.Text = "当前批次已经处理完成。";
        UpdateWorkbenchState();
    }

    private void SaveHistoryEntry(OrderDraft draft, string responseText, string actionType)
    {
        var entry = new OrderHistoryEntry
        {
            DraftId = draft.DraftId,
            OrderNumber = draft.OrderNumber,
            SessionId = draft.SessionId,
            Timestamp = DateTime.Now,
            ReceiverName = draft.ReceiverName,
            ReceiverMobile = draft.ReceiverMobile,
            ReceiverAddress = draft.ReceiverAddress,
            GoodsSummary = draft.GoodsSummary,
            Status = draft.Status,
            StatusDetail = draft.StatusDetail,
            OperatorLoginName = draft.OperatorLoginName,
            RawText = draft.RawText,
            ResponseText = responseText
        };

        _historyRepository.Upsert(entry);
        _auditRepository.Append(new OrderAuditRecord
        {
            RecordId = $"{draft.DraftId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            DraftId = draft.DraftId,
            OrderNumber = draft.OrderNumber,
            SessionId = draft.SessionId,
            Timestamp = DateTime.Now,
            ActionType = actionType,
            ReceiverName = draft.ReceiverName,
            ReceiverMobile = draft.ReceiverMobile,
            ReceiverAddress = draft.ReceiverAddress,
            GoodsSummary = draft.GoodsSummary,
            Status = draft.Status,
            OperatorLoginName = draft.OperatorLoginName,
            RawText = draft.RawText,
            SnapshotJson = BuildDraftSnapshotJson(draft),
            ResponseText = responseText
        });
        LoadHistory();
    }

    private static string BuildDraftSnapshotJson(OrderDraft draft)
    {
        var snapshot = new
        {
            draft.DraftId,
            draft.OrderNumber,
            draft.SessionId,
            draft.OrderIndex,
            draft.Status,
            draft.StatusDetail,
            draft.ReceiverName,
            draft.ReceiverMobile,
            draft.ReceiverAddress,
            draft.Remark,
            draft.HasGift,
            draft.OperatorLoginName,
            draft.OperatorErpId,
            Items = draft.Items.Select(item => new
            {
                item.SourceText,
                item.ProductCode,
                item.ProductName,
                item.BarcodeText,
                item.WearPeriod,
                item.QuantityText,
                item.Remark,
                item.DegreeText,
                item.IsTrial,
                item.MatchHint
            }).ToList()
        };

        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private void RefreshDraftViews()
    {
        GridDraftOrders.Items.Refresh();
        GridDraftItems.Items.Refresh();
        TxtQueueSummary.Text = _draftOrders.Count == 0
            ? "当前还没有解析结果。"
            : $"共 {_draftOrders.Count} 条订单，其中上传成功 {_draftOrders.Count(item => item.Status == "上传成功")} 条。";
        UpdateWorkbenchState();
    }

    private void RefreshDraftResolution(OrderDraft draft)
    {
        _catalogSkuResolver.RefreshDraft(draft, BuildSnapshotFromUi());
    }

    private void RefreshAllDraftResolutions()
    {
        var snapshot = BuildSnapshotFromUi();
        foreach (var draft in _draftOrders)
        {
            _catalogSkuResolver.RefreshDraft(draft, snapshot);
        }

        GridDraftItems.Items.Refresh();
        GridDraftOrders.Items.Refresh();
        UpdateWorkbenchState();
    }

    private void UpdateWorkbenchState()
    {
        UpdateSelectedDraftSummary(_selectedDraft);

        var total = _draftOrders.Count;
        var completedCount = _draftOrders.Count(item =>
            string.Equals(item.Status, "上传成功", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Status, "已跳过", StringComparison.OrdinalIgnoreCase));
        var waitingCount = _draftOrders.Count(item => string.Equals(item.Status, "待审核", StringComparison.OrdinalIgnoreCase));
        var fixCount = _draftOrders.Count(item => string.Equals(item.Status, "待补全", StringComparison.OrdinalIgnoreCase));
        var readyCount = _draftOrders.Count(item => string.Equals(item.Status, "待上传", StringComparison.OrdinalIgnoreCase));
        var failedCount = _draftOrders.Count(item => string.Equals(item.Status, "上传失败", StringComparison.OrdinalIgnoreCase));

        TxtWorkbenchSummary.Text = total == 0
            ? "当前批次还没有订单草稿。"
            : $"共 {total} 条，待审核 {waitingCount} 条，待补全 {fixCount} 条，待上传 {readyCount} 条，异常 {failedCount} 条，已完成 {completedCount} 条。";
        TxtFlowHint.Text = BuildFlowHint();

        UpdateActionAvailability();
    }

    private void UpdateSelectedDraftSummary(OrderDraft? draft)
    {
        if (draft is null)
        {
            TxtCurrentDraftHeadline.Text = "尚未选择订单";
            TxtCurrentDraftMeta.Text = "先解析文本，再从队列中选择一条订单开始审核。";
            TxtDraftEditorSummary.Text = "未选中订单。右侧会显示当前订单的收件信息、商品和校验状态。";
            return;
        }

        TxtCurrentDraftHeadline.Text = $"{DisplayValue(draft.OrderNumber, $"订单 #{draft.OrderIndex}")} · {draft.Status}";
        TxtCurrentDraftMeta.Text =
            $"{DisplayValue(draft.ReceiverName, "未填写收件人")} / {DisplayValue(draft.ReceiverMobile, "未填写联系电话")} / 商品 {draft.Items.Count} 项 / 编号 {DisplayValue(draft.OrderNumber, "待生成")}";

        var summaryBuilder = new StringBuilder()
            .Append("订单号：").Append(DisplayValue(draft.OrderNumber, "待生成"))
            .Append("  |  ")
            .Append("收件人：").Append(DisplayValue(draft.ReceiverName, "未填写"))
            .Append("  |  地址：").Append(DisplayValue(draft.ReceiverAddress, "未填写"))
            .Append("  |  业务员：").Append(DisplayValue(draft.OperatorLoginName, "未选择"))
            .Append("  |  赠品：").Append(draft.HasGift ? "有" : "无");

        if (!string.IsNullOrWhiteSpace(draft.ParseWarnings))
        {
            summaryBuilder.AppendLine()
                .Append("解析提示：")
                .Append(draft.ParseWarnings);
        }

        if (!string.IsNullOrWhiteSpace(draft.StatusDetail))
        {
            summaryBuilder.AppendLine()
                .Append("当前说明：")
                .Append(draft.StatusDetail);
        }

        TxtDraftEditorSummary.Text = summaryBuilder.ToString();
    }

    private void UpdateActionAvailability()
    {
        var hasDrafts = _draftOrders.Count > 0;
        var hasSelectedDraft = _selectedDraft is not null;
        var hasRunnableDraft = _draftOrders.Any(item =>
            !string.Equals(item.Status, "上传成功", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(item.Status, "已跳过", StringComparison.OrdinalIgnoreCase));

        BtnSaveParseRecord.IsEnabled = hasDrafts;
        BtnValidateCurrent.IsEnabled = hasSelectedDraft;
        BtnSubmitCurrent.IsEnabled = hasSelectedDraft;
        BtnSkipCurrent.IsEnabled = hasSelectedDraft;
        BtnAddItem.IsEnabled = hasSelectedDraft;
        BtnRemoveItem.IsEnabled = hasSelectedDraft && GridDraftItems.SelectedItem is OrderItemDraft;
        BtnSubmitAll.IsEnabled = hasRunnableDraft;
    }

    private string BuildFlowHint()
    {
        if (_draftOrders.Count == 0)
        {
            return "先在左侧粘贴订单文本，点击“解析文本”生成草稿。";
        }

        if (_selectedDraft is null)
        {
            return "当前已有草稿，下一步从中间队列选择一条订单并开始审核。";
        }

        return _selectedDraft.Status switch
        {
            "待补全" => "当前订单存在缺失项，先在右侧补全收件信息或商品字段，再重新校验。",
            "待上传" => "当前订单已通过校验，可以直接上传；确认无误后也可继续处理下一条。",
            "上传成功" => "当前订单已上传成功，系统会优先切换到下一条未完成订单。",
            "上传失败" => "当前订单上传失败，先查看下方上传响应，再决定修正后重试。",
            "已跳过" => "当前订单已标记为跳过，可继续处理后续订单。",
            _ => "建议先执行“校验当前订单”，确认字段完整后再上传。"
        };
    }

    private UserAccountRow? FindUserAccount(string? loginName)
    {
        if (string.IsNullOrWhiteSpace(loginName))
        {
            return null;
        }

        return _userAccounts.FirstOrDefault(item => string.Equals(item.LoginName, loginName, StringComparison.OrdinalIgnoreCase));
    }

    private void AssignDraftOrderNumbers()
    {
        var sequenceByAccount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var draft in _draftOrders.OrderBy(item => item.OrderIndex))
        {
            var accountToken = NormalizeOrderAccount(draft.OperatorLoginName);
            var next = sequenceByAccount.TryGetValue(accountToken, out var current) ? current + 1 : 1;
            sequenceByAccount[accountToken] = next;
            draft.OrderNumber = $"lenspop{accountToken}{next:D4}";
        }
    }

    private static void ApplyProductCodeComboFilter(ComboBox comboBox, string? keyword)
    {
        if (comboBox.ItemsSource is null)
        {
            return;
        }

        var compactKeyword = MatchTextHelper.Compact(keyword);
        var initialKeyword = Regex.Replace(keyword ?? string.Empty, @"[^A-Za-z0-9]", string.Empty).ToLowerInvariant();
        var view = CollectionViewSource.GetDefaultView(comboBox.ItemsSource);
        view.Filter = item => item is ProductCodeOption option && MatchesProductCodeOption(option, compactKeyword, initialKeyword);
        view.Refresh();
    }

    private static bool MatchesProductCodeOption(ProductCodeOption option, string compactKeyword, string initialKeyword)
    {
        if (string.IsNullOrWhiteSpace(compactKeyword) && string.IsNullOrWhiteSpace(initialKeyword))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(compactKeyword) &&
            option.SearchText.Contains(compactKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(initialKeyword) &&
               option.Initials.Contains(initialKeyword, StringComparison.OrdinalIgnoreCase);
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T matched)
            {
                return matched;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private static string NormalizeOrderAccount(string? loginName)
    {
        var compact = Regex.Replace(Safe(loginName).ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
        return string.IsNullOrWhiteSpace(compact) ? "user" : compact;
    }

    private static string Safe(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string DisplayValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
