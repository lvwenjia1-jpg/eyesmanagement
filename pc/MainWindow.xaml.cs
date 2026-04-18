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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    private readonly RuleRepository _ruleRepository = new();
    private readonly WorkflowSettingsRepository _settingsRepository = new();
    private readonly ProductCatalogRepository _productCatalogRepository = new();
    private readonly OrderHistoryRepository _historyRepository = new();
    private readonly OrderAuditRepository _auditRepository = new();
    private readonly UploadLearningSampleRepository _uploadLearningSampleRepository = new();
    private readonly OrderDraftFactory _draftFactory = new();
    private readonly OrderDraftValidator _draftValidator = new();
    private readonly CatalogSkuResolver _catalogSkuResolver = new();
    private readonly HupunB2cTradeUploader _tradeUploader = new();
    private readonly MainApiSyncClient _mainApiSyncClient = new();
    private readonly MainApiSession? _session;

    private ParserRuleSet _ruleSet = ParserRuleSet.CreateDefault();
    private ObservableCollection<LookupValueRow> _wearPeriods = new();
    private ObservableCollection<WearPeriodMappingRow> _wearMappings = new();
    private ObservableCollection<ProductCatalogEntry> _productCatalog = new();
    private ObservableCollection<ProductCatalogGroupRow> _productCatalogGroups = new();
    private ObservableCollection<ProductCatalogDegreeRow> _productCatalogDegrees = new();
    private ObservableCollection<ProductCodeMappingRow> _productMappings = new();
    private ObservableCollection<UserAccountRow> _userAccounts = new();
    private ObservableCollection<BusinessGroupOption> _businessGroups = new();
    private ObservableCollection<OrderDraft> _draftOrders = new();
    private ObservableCollection<OrderAuditRecord> _historyEntries = new();
    private ObservableCollection<TrainingOrderDefinition> _trainingOrders = new();
    private ParseResult? _lastParseResult;
    private UploadConfiguration _uploadConfiguration = new();
    private MainApiConfiguration _mainApiConfiguration = new();
    private OrderDraft? _selectedDraft;
    private ProductCodePickerWindow? _openProductCodePicker;
    private OrderItemDraft? _openProductCodePickerItem;
    private bool _isApplyingLoggedInAccount;
    private bool _isParsing;
    private const int DefaultProductCodeVisibleCount = 60;
    private const int ParseDraftBatchSize = 1;
    private const string ProductCodeComboSuppressToken = "__product-code-suppress__";

    public MainWindow(MainApiSession? session = null)
    {
        _session = session;
        InitializeComponent();
        LoadSettingsIntoUi(_settingsRepository.LoadOrCreate());
        LoadHistory();
        Loaded += MainWindow_Loaded;

        TxtInput.Text = SampleData.DefaultText;
        TxtParseSummary.Text = "把一段或多段订单文本贴到左侧，系统会拆成多条订单草稿。";
        TxtQueueSummary.Text = "当前还没有解析结果。";
        TxtValidationOutput.Text = "待校验。";
        TxtUploadOutput.Text = "待上传。";
        TxtTradeQueryResult.Text = "订单查询结果会显示在这里。";
        TxtCurrentRawOrder.Text = "尚未选择订单。";
        TxtCurrentDraftHeadline.Text = "尚未选择订单";
        TxtCurrentDraftMeta.Text = "先解析文本，再从队列中选择一条订单开始审核。";
        TxtDraftEditorSummary.Text = "未选中订单。右侧会显示当前订单的收件信息、商品和校验状态。";
        TxtProductWorkflowSummary.Text = "商品编码工作流：未选中订单。";
        TxtProductWorkflowHint.Text = "解析后会按 周期 / 型号 / 度数 自动尝试直配商品编码。";
        TxtWorkbenchSummary.Text = "当前批次还没有订单草稿。";
        TxtFlowHint.Text = "先在左侧粘贴订单文本，点击“解析文本”生成草稿。";
        TxtHistoryRaw.Text = "请选择一条历史记录。";
        TxtHistorySnapshot.Text = "请选择一条历史记录。";
        TxtHistoryResponse.Text = "请选择一条历史记录。";
        TxtTrainingStatus.Text = "把训练输入和结构化字段整理好后，可以先生成当前解析结果，再保存样本或应用到语义。";
        GridTrainingOrders.ItemsSource = _trainingOrders;
        GridTrainingItems.ItemsSource = null;
        TxtStatus.Text = "准备就绪。";
        TxtSettingsStatus.Text = "设置已加载。";
        UpdateWorkbenchState();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLoggedInAccount();
        await LoadBusinessGroupsAsync();
    }

    private void ApplyLoggedInAccount()
    {
        if (_session?.User is null || string.IsNullOrWhiteSpace(_session.User.LoginName))
        {
            return;
        }

        var currentUser = new UserAccountRow
        {
            LoginName = _session.User.LoginName.Trim(),
            DisplayName = _session.User.LoginName.Trim(),
            ErpId = _session.User.ErpId?.Trim() ?? string.Empty
        };

        _userAccounts = new ObservableCollection<UserAccountRow>(new[] { currentUser });

        _isApplyingLoggedInAccount = true;
        try
        {
            RefreshLookupSources();
            CmbOperatorAccounts.SelectedItem = FindUserAccount(currentUser.LoginName);
            CmbDraftOperator.SelectedItem = FindUserAccount(currentUser.LoginName);
        }
        finally
        {
            _isApplyingLoggedInAccount = false;
        }

        TxtStatus.Text = $"当前登录账号：{currentUser.LoginName}";
    }

    private async Task LoadBusinessGroupsAsync()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            var groups = await _mainApiSyncClient.QueryBusinessGroupsAsync(_session.Configuration);
            _businessGroups = new ObservableCollection<BusinessGroupOption>(groups.Select(item => new BusinessGroupOption
            {
                Id = item.Id,
                Name = item.Name
            }));
            CmbBusinessGroups.ItemsSource = _businessGroups;
            if (_businessGroups.Count > 0 && CmbBusinessGroups.SelectedItem is null)
            {
                CmbBusinessGroups.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"业务群加载失败：{ex.Message}";
        }
    }

    private void ApplySelectedBusinessGroupToDrafts()
    {
        var selectedGroup = CmbBusinessGroups.SelectedItem as BusinessGroupOption;
        foreach (var draft in _draftOrders)
        {
            draft.BusinessGroupId = selectedGroup?.Id;
            draft.BusinessGroupName = selectedGroup?.Name ?? string.Empty;
        }

        if (_selectedDraft is not null)
        {
            UpdateSelectedDraftSummary(_selectedDraft);
        }

        GridDraftOrders.Items.Refresh();
    }

    private UserAccountRow? GetCurrentLoggedInAccount()
    {
        return _session?.User is null
            ? CmbOperatorAccounts.SelectedItem as UserAccountRow
            : FindUserAccount(_session.User.LoginName);
    }

    private async void BtnParse_Click(object sender, RoutedEventArgs e)
    {
        if (_isParsing)
        {
            return;
        }

        PersistSelectedDraftFromForm();
        var snapshot = BuildSnapshotFromUi();
        var selectedAccount = GetCurrentLoggedInAccount();
        var selectedAccountSnapshot = selectedAccount is null
            ? null
            : new UserAccountRow
            {
                LoginName = selectedAccount.LoginName,
                DisplayName = selectedAccount.DisplayName,
                ErpId = selectedAccount.ErpId
            };

        _isParsing = true;
        TxtStatus.Text = "正在后台解析订单文本，请稍候…";
        TxtParseSummary.Text = "正在解析订单文本…";
        _draftOrders.Clear();
        _selectedDraft = null;
        LoadDraftToForm(null);
        GridDraftOrders.Items.Refresh();
        UpdateActionAvailability();

        try
        {
            var rawText = TxtInput.Text;
            var parseTaskResult = await Task.Run(() =>
            {
                var drafts = _draftFactory.CreateDraftsInBatches(
                    rawText,
                    snapshot,
                    selectedAccountSnapshot,
                    ParseDraftBatchSize,
                    batch =>
                    {
                        _catalogSkuResolver.RefreshDrafts(batch, snapshot);
                        Dispatcher.Invoke(() => AppendDraftBatch(batch));
                    },
                    out var parseResult);
                return new ParseTaskResult(drafts, parseResult);
            });

            _lastParseResult = parseTaskResult.ParseResult;
            ApplySelectedBusinessGroupToDrafts();
            AssignDraftOrderNumbers();
            GridDraftOrders.Items.Refresh();

            TxtParseSummary.Text =
                $"识别 {parseTaskResult.ParseResult.Orders.Count} 个订单，未识别片段 {parseTaskResult.ParseResult.UnknownSegments.Count} 条，警告 {parseTaskResult.ParseResult.Warnings.Count} 条。";
            TxtQueueSummary.Text = _draftOrders.Count == 0
                ? "没有生成订单草稿。"
                : $"已生成 {_draftOrders.Count} 条订单草稿。上传成功后会自动切到下一条。";
            TxtValidationOutput.Text = parseTaskResult.ParseResult.Warnings.Count == 0
                ? "解析完成，等待审核。"
                : string.Join(Environment.NewLine, parseTaskResult.ParseResult.Warnings);
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

            TxtStatus.Text = $"解析完成，共 {_draftOrders.Count} 条订单草稿。";
            UpdateWorkbenchState();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"解析失败：{ex.Message}";
            TxtValidationOutput.Text = ex.Message;
            MessageBox.Show($"解析失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isParsing = false;
            UpdateActionAvailability();
        }
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

    private async void BtnQueryTradeRecords_Click(object sender, RoutedEventArgs e)
    {
        PersistSelectedDraftFromForm();
        BtnQueryTradeRecords.IsEnabled = false;
        try
        {
            var snapshot = BuildSnapshotFromUi();
            // Query button should fetch recent trade records, not be narrowed by the currently selected local draft.
            var result = await _tradeUploader.QueryTradeListAsync(draft: null, snapshot.Upload);
            var displayText = BuildTradeQueryDisplayText(result);
            TxtTradeQueryResult.Text = displayText;
            TxtUploadOutput.Text = displayText;
            TxtStatus.Text = result.IsSuccess ? "订单记录查询完成。" : "订单记录查询已返回，请检查接口结果。";
        }
        catch (Exception ex)
        {
            TxtTradeQueryResult.Text = ex.ToString();
            TxtUploadOutput.Text = ex.ToString();
            TxtStatus.Text = "订单记录查询失败。";
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

    private void BtnTrainingUseCurrent_Click(object sender, RoutedEventArgs e)
    {
        TxtTrainingInput.Text = TxtInput.Text;
        TxtTrainingStatus.Text = "已带入当前订单文本。";
    }

    private void BtnTrainingGenerate_Click(object sender, RoutedEventArgs e)
    {
        var rawText = TxtTrainingInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            MessageBox.Show("请先输入训练原文。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var snapshot = BuildSnapshotFromUi();
        var drafts = _draftFactory.CreateDrafts(rawText, snapshot, null, out _);
        var generated = drafts.Select(draft => new TrainingOrderDefinition
        {
            ReceiverName = draft.ReceiverName,
            ReceiverMobile = draft.ReceiverMobile,
            ReceiverAddress = draft.ReceiverAddress,
            WearPeriod = draft.Items.Select(item => item.WearPeriod).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
            Items = new ObservableCollection<TrainingItemDefinition>(
                draft.Items.Select(item => new TrainingItemDefinition
                {
                    ProductName = item.ProductName,
                    WearPeriod = item.WearPeriod,
                    Degree = item.DegreeText,
                    Quantity = int.TryParse(item.QuantityText, out var quantity) ? quantity : 1
                }))
        }).ToList();
        SetTrainingOrders(generated);
        TxtTrainingStatus.Text = "已生成当前解析结果，你可以在右侧直接修正结构化字段后再应用。";
    }

    private void BtnTrainingSaveSample_Click(object sender, RoutedEventArgs e)
    {
        var rawText = TxtTrainingInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            MessageBox.Show("请先输入训练原文。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var trainingOrders = BuildTrainingOrdersFromEditor();
        var expectedOutput = SerializeTrainingOrders(trainingOrders);

        _ruleRepository.AppendSample(new TrainingSample
        {
            RawText = rawText,
            Notes = string.IsNullOrWhiteSpace(expectedOutput) ? null : expectedOutput
        });

        TxtTrainingStatus.Text = "训练样本已保存到 training-samples.jsonl。";
    }

    private void BtnTrainingApply_Click(object sender, RoutedEventArgs e)
    {
        var rawText = TxtTrainingInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            MessageBox.Show("训练输入和结构化输出都需要填写。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var trainingOrders = BuildTrainingOrdersFromEditor();
        if (trainingOrders.Count == 0)
        {
            MessageBox.Show("结构化训练输出未整理出有效订单。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var parser = new OrderTextParser();
        var productAliasAdded = 0;
        var wearAliasAdded = 0;

        foreach (var order in trainingOrders)
        {
            foreach (var alias in order.WearAliases.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                wearAliasAdded += AddWearAlias(alias, order.WearPeriod);
            }

            foreach (var item in order.Items)
            {
                foreach (var alias in item.WearAliases.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    wearAliasAdded += AddWearAlias(alias, item.WearPeriod);
                }

                var canonicalName = Safe(item.ProductName);
                var aliases = item.Aliases
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(Safe)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (string.IsNullOrWhiteSpace(canonicalName) || aliases.Count == 0)
                {
                    continue;
                }

                var before = _ruleSet.ProductAliases.Count;
                parser.AddOrUpdateProductAlias(_ruleSet, canonicalName, aliases);
                if (_ruleSet.ProductAliases.Count >= before)
                {
                    foreach (var alias in aliases)
                    {
                        productAliasAdded += AddProductAliasMapping(alias, canonicalName);
                    }
                }
            }
        }

        var expectedOutput = SerializeTrainingOrders(trainingOrders);
        _ruleRepository.AppendSample(new TrainingSample
        {
            RawText = rawText,
            Notes = expectedOutput
        });

        var snapshot = BuildSnapshotFromUi();
        _settingsRepository.Save(snapshot);
        RefreshLookupSources();
        RefreshAllDraftResolutions();
        TxtTrainingStatus.Text = $"已应用训练语义：新增商品别名 {productAliasAdded} 条，新增周期别名 {wearAliasAdded} 条，并已保存训练样本。";
        TxtSettingsStatus.Text = "训练语义已写入当前设置。";
        UpdateWorkbenchState();
    }

    private void GridTrainingOrders_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridTrainingOrders.SelectedItem is TrainingOrderDefinition order)
        {
            GridTrainingItems.ItemsSource = order.Items;
            TxtTrainingStructuredHint.Text = $"当前正在编辑 {DisplayValue(order.ReceiverName, "未填收件人")} 的训练结果，共 {order.Items.Count} 个商品。";
            return;
        }

        GridTrainingItems.ItemsSource = null;
        TxtTrainingStructuredHint.Text = "先点“生成当前解析结果”，再在这里直接改收件信息、周期、别名、商品和度数。";
    }

    private void BtnImportCatalog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入商品列表",
            Filter = "Excel 商品表 (*.xlsx)|*.xlsx|JSON 商品表 (*.json)|*.json|所有文件 (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var selectedFiles = dialog.FileNames
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var entries = _productCatalogRepository.ImportFromFiles(selectedFiles);

            _productCatalog = new ObservableCollection<ProductCatalogEntry>(entries);
            RebuildProductCatalogView();
            var snapshot = BuildSnapshotFromUi();
            _settingsRepository.Save(snapshot);
            RefreshLookupSources();
            RefreshAllDraftResolutions();
            TxtStatus.Text = $"已导入商品列表，共 {_productCatalog.Count} 条商品编码，来源文件 {selectedFiles.Length} 个。";
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
                TxtSettingsStatus.Text = "商品编码列表请通过导入 Excel 商品表维护。";
                return;
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
            case 2:
                TxtSettingsStatus.Text = "商品编码列表请通过重新导入维护。";
                return;
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
        if (_isApplyingLoggedInAccount)
        {
            return;
        }

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

    private void CmbBusinessGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySelectedBusinessGroupToDrafts();
        if (CmbBusinessGroups.SelectedItem is BusinessGroupOption selectedGroup)
        {
            TxtStatus.Text = $"当前业务群：{selectedGroup.Name}";
        }
    }

    private void CmbDraftOperator_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingLoggedInAccount)
        {
            return;
        }

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

    private void GridDraftItems_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DraftEditorScrollViewer is null || Math.Abs(e.Delta) < 1)
        {
            return;
        }

        var nextOffset = DraftEditorScrollViewer.VerticalOffset - (e.Delta / 3d);
        nextOffset = Math.Max(0, Math.Min(DraftEditorScrollViewer.ScrollableHeight, nextOffset));
        if (Math.Abs(nextOffset - DraftEditorScrollViewer.VerticalOffset) < 0.1d)
        {
            return;
        }

        DraftEditorScrollViewer.ScrollToVerticalOffset(nextOffset);
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
            GridDraftOrders.Items.Refresh();
            UpdateWorkbenchState();
        }), DispatcherPriority.ContextIdle);
    }

    private void ProductCodeCellButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDraft is null || sender is not FrameworkElement element || element.DataContext is not OrderItemDraft item)
        {
            return;
        }

        if (_openProductCodePicker is { IsVisible: true })
        {
            if (ReferenceEquals(_openProductCodePickerItem, item))
            {
                _openProductCodePicker.Activate();
                return;
            }

            _openProductCodePicker.Close();
        }

        var selectedDraft = _selectedDraft;
        if (selectedDraft is null)
        {
            return;
        }

        var snapshot = BuildSnapshotFromUi();
        var picker = new ProductCodePickerWindow(item, snapshot)
        {
            Owner = this
        };

        _openProductCodePicker = picker;
        _openProductCodePickerItem = item;

        picker.Confirmed += (_, args) =>
        {
            item.ProductCode = args.SelectedOption.ProductCode;
            item.ProductCodeSearchKeyword = args.ConfirmedKeyword;
            item.ProductCodeConfirmed = true;
            item.MatchHint = $"已确认商品编码：{args.SelectedOption.ProductCode}";

            RefreshDraftResolution(selectedDraft);
            GridDraftItems.Items.Refresh();
            GridDraftOrders.Items.Refresh();
            UpdateSelectedDraftSummary(selectedDraft);
            UpdateWorkbenchState();
        };

        picker.Closed += (_, _) =>
        {
            if (ReferenceEquals(_openProductCodePicker, picker))
            {
                _openProductCodePicker = null;
                _openProductCodePickerItem = null;
            }
        };

        picker.Show();
        picker.Activate();
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

            ApplyProductCodeComboFilter(comboBox, string.Empty);
        }
    }

    private void ProductCodeComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            ApplyProductCodeComboFilter(comboBox, string.Empty);
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
        if (sender is not ComboBox comboBox || comboBox.SelectedItem is not ProductCodeOption)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            comboBox.Tag = ProductCodeComboSuppressToken;
            comboBox.IsDropDownOpen = false;
            ApplyProductCodeComboFilter(comboBox, string.Empty);
            GridDraftItems.CommitEdit(DataGridEditingUnit.Cell, true);
        }), DispatcherPriority.Background);
    }

    private void ProductCodeComboBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            comboBox.IsDropDownOpen = false;
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

        if (Equals(comboBox.Tag, ProductCodeComboSuppressToken))
        {
            comboBox.Tag = null;
            return;
        }

        if (!editor.IsKeyboardFocusWithin)
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

        if (CmbBusinessGroups.SelectedItem is BusinessGroupOption selectedGroup)
        {
            _selectedDraft.BusinessGroupId = selectedGroup.Id;
            _selectedDraft.BusinessGroupName = selectedGroup.Name;
        }
        else
        {
            _selectedDraft.BusinessGroupId = null;
            _selectedDraft.BusinessGroupName = string.Empty;
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
            UpdateProductWorkflowPanel(null);
            UpdateActionAvailability();
            return;
        }

        TxtDraftReceiverName.Text = draft.ReceiverName;
        TxtDraftReceiverMobile.Text = draft.ReceiverMobile;
        TxtDraftReceiverAddress.Text = draft.ReceiverAddress;
        TxtDraftRemark.Text = draft.Remark;
        ChkDraftHasGift.IsChecked = draft.HasGift;
        CmbDraftOperator.SelectedItem = FindUserAccount(draft.OperatorLoginName) ?? CmbOperatorAccounts.SelectedItem;
        if (draft.BusinessGroupId.HasValue)
        {
            CmbBusinessGroups.SelectedItem = _businessGroups.FirstOrDefault(item => item.Id == draft.BusinessGroupId.Value);
        }
        GridDraftItems.ItemsSource = draft.Items;
        TxtCurrentRawOrder.Text = draft.RawText;
        TxtValidationOutput.Text = string.IsNullOrWhiteSpace(draft.StatusDetail)
            ? "待校验。"
            : draft.StatusDetail;
        UpdateSelectedDraftSummary(draft);
        UpdateProductWorkflowPanel(draft);
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
        _mainApiConfiguration = snapshot.MainApi ?? new MainApiConfiguration();
        if (_session is not null)
        {
            _mainApiConfiguration.BaseUrl = _session.Configuration.BaseUrl;
            _mainApiConfiguration.LoginName = _session.Configuration.LoginName;
            _mainApiConfiguration.Password = _session.Configuration.Password;
            _mainApiConfiguration.MachineCode = _session.Configuration.MachineCode;
        }

        GridWearPeriods.ItemsSource = _wearPeriods;
        GridWearMappings.ItemsSource = _wearMappings;
        RebuildProductCatalogView();
        GridProductMappings.ItemsSource = _productMappings;
        GridUserAccounts.ItemsSource = _userAccounts;
        GridDraftOrders.ItemsSource = _draftOrders;
        GridHistory.ItemsSource = _historyEntries;

        TxtMainApiBaseUrl.Text = _mainApiConfiguration.BaseUrl;
        TxtUploadApiUrl.Text = _uploadConfiguration.ApiUrl;
        TxtUploadAppKey.Text = _uploadConfiguration.AppKey;
        TxtUploadSecret.Text = _uploadConfiguration.Secret;
        TxtUploadShopNick.Text = _uploadConfiguration.ShopNick;
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

    private void RebuildProductCatalogView()
    {
        _productCatalogGroups = new ObservableCollection<ProductCatalogGroupRow>(
            _productCatalog
                .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
                .GroupBy(item => new
                {
                    WearPeriod = ResolveCatalogWearPeriod(item),
                    ModelName = ResolveCatalogModelName(item)
                })
                .Select(group => new ProductCatalogGroupRow
                {
                    WearPeriod = group.Key.WearPeriod,
                    ModelName = group.Key.ModelName,
                    Degrees = group
                        .Select(item => new ProductCatalogDegreeRow
                        {
                            DegreeText = string.IsNullOrWhiteSpace(item.Degree) ? "未分度数" : item.Degree.Trim(),
                            ProductCode = item.ProductCode.Trim()
                        })
                        .GroupBy(item => $"{item.DegreeText}|{item.ProductCode}", StringComparer.OrdinalIgnoreCase)
                        .Select(item => item.First())
                        .OrderBy(item => SortDegree(item.DegreeText))
                        .ThenBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .Select(group =>
                {
                    group.DegreeCount = group.Degrees.Count;
                    return group;
                })
                .OrderBy(group => group.WearPeriod, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => group.ModelName, StringComparer.OrdinalIgnoreCase)
                .ToList());

        GridProductCatalog.ItemsSource = _productCatalogGroups;

        var firstGroup = _productCatalogGroups.FirstOrDefault();
        GridProductCatalog.SelectedItem = firstGroup;
        ShowProductCatalogDegrees(firstGroup);
    }

    private void GridProductCatalog_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowProductCatalogDegrees(GridProductCatalog.SelectedItem as ProductCatalogGroupRow);
    }

    private void BtnShowProductCatalogDegrees_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ProductCatalogGroupRow group)
        {
            GridProductCatalog.SelectedItem = group;
            GridProductCatalog.ScrollIntoView(group);
            ShowProductCatalogDegrees(group);
        }
    }

    private void ShowProductCatalogDegrees(ProductCatalogGroupRow? group)
    {
        _productCatalogDegrees = new ObservableCollection<ProductCatalogDegreeRow>(group?.Degrees ?? new List<ProductCatalogDegreeRow>());
        GridProductCatalogDegrees.ItemsSource = _productCatalogDegrees;
        TxtProductCatalogDetailTitle.Text = group is null
            ? "度数明细"
            : $"{DisplayValue(group.WearPeriod, "未识别周期")} / {DisplayValue(group.ModelName, "未识别型号")} · {_productCatalogDegrees.Count} 条商品编码";
    }

    private static string ResolveCatalogWearPeriod(ProductCatalogEntry entry)
    {
        var value = Safe(entry.SpecificationToken);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var code = Safe(entry.ProductCode);
        if (code.Contains("半年抛", StringComparison.OrdinalIgnoreCase)) return "半年抛";
        if (code.Contains("年抛", StringComparison.OrdinalIgnoreCase)) return "年抛";
        if (code.Contains("日抛10片", StringComparison.OrdinalIgnoreCase)) return "日抛10片";
        if (code.Contains("日抛2片", StringComparison.OrdinalIgnoreCase)) return "日抛2片";
        if (code.Contains("日抛", StringComparison.OrdinalIgnoreCase)) return "日抛";
        if (code.Contains("试戴片", StringComparison.OrdinalIgnoreCase)) return "试戴片";
        return "未识别周期";
    }

    private static string ResolveCatalogModelName(ProductCatalogEntry entry)
    {
        var value = Safe(entry.ModelToken);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var baseName = Safe(entry.BaseName);
        return string.IsNullOrWhiteSpace(baseName) ? Safe(entry.ProductCode) : baseName;
    }

    private static int SortDegree(string degreeText)
    {
        return int.TryParse(Regex.Replace(degreeText ?? string.Empty, @"[^\d-]", string.Empty), out var degree)
            ? degree
            : int.MaxValue;
    }

    private int AddWearAlias(string alias, string? wearPeriod)
    {
        var cleanAlias = Safe(alias);
        var cleanWearPeriod = Safe(wearPeriod);
        if (string.IsNullOrWhiteSpace(cleanAlias) || string.IsNullOrWhiteSpace(cleanWearPeriod))
        {
            return 0;
        }

        if (!_wearPeriods.Any(item => string.Equals(item.Value, cleanWearPeriod, StringComparison.OrdinalIgnoreCase)))
        {
            _wearPeriods.Add(new LookupValueRow { Value = cleanWearPeriod });
        }

        if (_wearMappings.Any(item =>
                string.Equals(item.Alias, cleanAlias, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.WearPeriod, cleanWearPeriod, StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        _wearMappings.Add(new WearPeriodMappingRow
        {
            Alias = cleanAlias,
            WearPeriod = cleanWearPeriod
        });
        return 1;
    }

    private int AddProductAliasMapping(string alias, string canonicalName)
    {
        var cleanAlias = Safe(alias);
        var cleanCanonicalName = Safe(canonicalName);
        if (string.IsNullOrWhiteSpace(cleanAlias) || string.IsNullOrWhiteSpace(cleanCanonicalName))
        {
            return 0;
        }

        var catalogEntry = _productCatalog.FirstOrDefault(entry =>
            string.Equals(Safe(entry.ProductName), cleanCanonicalName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Safe(entry.BaseName), cleanCanonicalName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Safe(entry.ModelToken), cleanCanonicalName, StringComparison.OrdinalIgnoreCase));

        var productCode = catalogEntry?.ProductCode ?? string.Empty;
        if (_productMappings.Any(item =>
                string.Equals(item.Alias, cleanAlias, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Safe(item.ProductCode), Safe(productCode), StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        _productMappings.Add(new ProductCodeMappingRow
        {
            Alias = cleanAlias,
            ProductCode = productCode,
            Note = cleanCanonicalName
        });
        return 1;
    }

    private void SetTrainingOrders(IEnumerable<TrainingOrderDefinition> orders)
    {
        _trainingOrders = new ObservableCollection<TrainingOrderDefinition>(
            orders.Select(CloneTrainingOrder));
        GridTrainingOrders.ItemsSource = _trainingOrders;
        GridTrainingOrders.SelectedItem = _trainingOrders.FirstOrDefault();
        GridTrainingItems.ItemsSource = (GridTrainingOrders.SelectedItem as TrainingOrderDefinition)?.Items;
    }

    private List<TrainingOrderDefinition> BuildTrainingOrdersFromEditor()
    {
        return _trainingOrders
            .Select(CloneTrainingOrder)
            .Where(order =>
                !string.IsNullOrWhiteSpace(order.WearPeriod) ||
                !string.IsNullOrWhiteSpace(order.ReceiverName) ||
                order.Items.Any(item =>
                    !string.IsNullOrWhiteSpace(item.ProductName) ||
                    !string.IsNullOrWhiteSpace(item.Degree) ||
                    item.Quantity > 0 ||
                    item.Aliases.Count > 0 ||
                    item.WearAliases.Count > 0))
            .ToList();
    }

    private static string SerializeTrainingOrders(List<TrainingOrderDefinition> trainingOrders)
    {
        if (trainingOrders.Count == 0)
        {
            return string.Empty;
        }

        object payload = trainingOrders.Count == 1 ? trainingOrders[0] : trainingOrders;
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static TrainingOrderDefinition CloneTrainingOrder(TrainingOrderDefinition source)
    {
        return new TrainingOrderDefinition
        {
            ReceiverName = Safe(source.ReceiverName),
            ReceiverMobile = Safe(source.ReceiverMobile),
            ReceiverAddress = Safe(source.ReceiverAddress),
            WearPeriod = Safe(source.WearPeriod),
            WearAliases = TrainingTextHelper.Split(source.WearAliasesText),
            Items = new ObservableCollection<TrainingItemDefinition>(
                source.Items.Select(CloneTrainingItem))
        };
    }

    private static TrainingItemDefinition CloneTrainingItem(TrainingItemDefinition source)
    {
        return new TrainingItemDefinition
        {
            ProductName = Safe(source.ProductName),
            WearPeriod = Safe(source.WearPeriod),
            Degree = Safe(source.Degree),
            Quantity = source.Quantity <= 0 ? 1 : source.Quantity,
            Aliases = TrainingTextHelper.Split(source.AliasesText),
            WearAliases = TrainingTextHelper.Split(source.WearAliasesText)
        };
    }

    private WorkflowSettingsSnapshot BuildSnapshotFromUi()
    {
        _mainApiConfiguration = new MainApiConfiguration
        {
            BaseUrl = TxtMainApiBaseUrl.Text.Trim(),
            LoginName = _session?.Configuration.LoginName ?? _mainApiConfiguration.LoginName,
            Password = _session?.Configuration.Password ?? _mainApiConfiguration.Password,
            MachineCode = _session?.Configuration.MachineCode ?? _mainApiConfiguration.MachineCode
        };

        _uploadConfiguration = new UploadConfiguration
        {
            ApiUrl = TxtUploadApiUrl.Text.Trim(),
            AppKey = TxtUploadAppKey.Text.Trim(),
            Secret = TxtUploadSecret.Text.Trim(),
            ShopNick = TxtUploadShopNick.Text.Trim(),
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
            Upload = _uploadConfiguration,
            MainApi = _mainApiConfiguration
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
        if (!draft.BusinessGroupId.HasValue || string.IsNullOrWhiteSpace(draft.BusinessGroupName))
        {
            draft.Status = "待补全";
            draft.StatusDetail = "请选择右上角业务群后再上传。";
            TxtUploadOutput.Text = draft.StatusDetail;
            RefreshDraftViews();
            return;
        }

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
            draft.StatusDetail = string.IsNullOrWhiteSpace(result.FriendlyMessage)
                ? result.DebugText
                : $"{result.FriendlyMessage}{Environment.NewLine}{Environment.NewLine}{result.DebugText}";

            try
            {
                await _mainApiSyncClient.SyncUploadAsync(
                    draft,
                    snapshot.MainApi,
                    JsonSerializer.Serialize(result.RequestFields, JsonOptions),
                    result.ResponseText);
            }
            catch (Exception syncEx)
            {
                draft.StatusDetail = $"{draft.StatusDetail}{Environment.NewLine}{Environment.NewLine}MainApi 记录失败：{syncEx.Message}";
            }

            TxtValidationOutput.Text = validation.ToString();
            TxtUploadOutput.Text = draft.StatusDetail;
            SaveHistoryEntry(draft, draft.StatusDetail, result.IsSuccess ? "上传成功" : "上传失败");
            if (result.IsSuccess)
            {
                SaveUploadLearningSample(draft, result);
            }
        }
        catch (Exception ex)
        {
            try
            {
                await _mainApiSyncClient.SyncUploadAsync(
                    draft,
                    snapshot.MainApi,
                    "{}",
                    ex.ToString());
            }
            catch
            {
            }

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
        if (!ShouldPersistHistory(actionType))
        {
            return;
        }

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
            OperatorErpId = draft.OperatorErpId,
            BusinessGroupId = draft.BusinessGroupId,
            BusinessGroupName = draft.BusinessGroupName,
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
            OperatorErpId = draft.OperatorErpId,
            BusinessGroupId = draft.BusinessGroupId,
            BusinessGroupName = draft.BusinessGroupName,
            RawText = draft.RawText,
            SnapshotJson = BuildDraftSnapshotJson(draft),
            ResponseText = responseText
        });
        LoadHistory();
    }

    private static bool ShouldPersistHistory(string? actionType)
    {
        return !string.Equals(actionType, "解析生成", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(actionType, "校验订单", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(actionType, "校验未通过", StringComparison.OrdinalIgnoreCase);
    }

    private void SaveUploadLearningSample(OrderDraft draft, HupunUploadAttemptResult result)
    {
        try
        {
            var tradeDetailsJson = ExtractUploadedOrdersJson(result.RequestFields);

            var record = new UploadLearningSampleRecord
            {
                RecordId = $"{draft.DraftId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                Timestamp = DateTime.Now,
                DraftId = draft.DraftId,
                OrderNumber = draft.OrderNumber,
                SessionId = draft.SessionId,
                ReceiverName = draft.ReceiverName,
                ReceiverMobile = draft.ReceiverMobile,
                ReceiverAddress = draft.ReceiverAddress,
                RawText = draft.RawText,
                RequestUrl = result.RequestUrl,
                IsSuccess = result.IsSuccess,
                ResponseText = result.ResponseText,
                TradeDetailsJson = tradeDetailsJson,
                DraftItems = draft.Items.Select(BuildDraftLearningItem).ToList(),
                UploadedItems = BuildUploadedLearningItems(draft, tradeDetailsJson)
            };

            _uploadLearningSampleRepository.Append(record);
        }
        catch
        {
            // Learning capture is best-effort and must not affect upload flow.
        }
    }

    private static UploadLearningItemRecord BuildDraftLearningItem(OrderItemDraft item)
    {
        return new UploadLearningItemRecord
        {
            SourceText = item.SourceText,
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            SpecCodeText = item.SpecCodeText,
            BarcodeText = item.BarcodeText,
            WearPeriod = item.WearPeriod,
            DegreeText = item.DegreeText,
            QuantityText = item.QuantityText,
            IsTrial = item.IsTrial
        };
    }

    private static List<UploadLearningItemRecord> BuildUploadedLearningItems(OrderDraft draft, string tradeDetailsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(tradeDetailsJson) ? "[]" : tradeDetailsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return draft.Items.Select(BuildDraftLearningItem).ToList();
            }

            var draftItems = draft.Items.ToList();
            var records = new List<UploadLearningItemRecord>();
            var index = 0;
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var draftItem = index < draftItems.Count ? draftItems[index] : null;
                records.Add(new UploadLearningItemRecord
                {
                    SourceText = draftItem?.SourceText ?? string.Empty,
                    ProductCode = row.TryGetProperty("item_id", out var itemId) ? itemId.ToString() : string.Empty,
                    ProductName = row.TryGetProperty("item_title", out var itemTitle)
                        ? itemTitle.ToString()
                        : draftItem?.ProductName ?? string.Empty,
                    SpecCodeText = draftItem?.SpecCodeText ?? string.Empty,
                    BarcodeText = draftItem?.BarcodeText ?? string.Empty,
                    WearPeriod = draftItem?.WearPeriod ?? string.Empty,
                    DegreeText = draftItem?.DegreeText ?? string.Empty,
                    QuantityText = row.TryGetProperty("size", out var quantity)
                        ? quantity.ToString()
                        : draftItem?.QuantityText ?? string.Empty,
                    IsTrial = draftItem?.IsTrial ?? false
                });
                index++;
            }

            return records;
        }
        catch
        {
            return draft.Items.Select(BuildDraftLearningItem).ToList();
        }
    }

    private static string ExtractUploadedOrdersJson(IReadOnlyDictionary<string, string> requestFields)
    {
        if (!requestFields.TryGetValue("trades", out var rawTrades) || string.IsNullOrWhiteSpace(rawTrades))
        {
            return "[]";
        }

        try
        {
            using var document = JsonDocument.Parse(rawTrades);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            {
                return "[]";
            }

            var firstTrade = document.RootElement[0];
            if (!firstTrade.TryGetProperty("orders", out var orders) || orders.ValueKind != JsonValueKind.Array)
            {
                return "[]";
            }

            return orders.GetRawText();
        }
        catch
        {
            return "[]";
        }
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
            draft.BusinessGroupId,
            draft.BusinessGroupName,
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

    private void AppendDraftBatch(IEnumerable<OrderDraft> batch)
    {
        var added = batch.ToList();
        if (added.Count == 0)
        {
            return;
        }

        foreach (var draft in added)
        {
            _draftOrders.Add(draft);
        }

        TxtQueueSummary.Text = $"已生成 {_draftOrders.Count} 条订单草稿，正在继续解析…";
        if (_selectedDraft is null && _draftOrders.Count > 0)
        {
            GridDraftOrders.SelectedIndex = 0;
        }
        UpdateWorkbenchState();
    }

    private void RefreshDraftResolution(OrderDraft draft)
    {
        _catalogSkuResolver.RefreshDraft(draft, BuildSnapshotFromUi());
    }

    private void RefreshAllDraftResolutions()
    {
        var snapshot = BuildSnapshotFromUi();
        _catalogSkuResolver.RefreshDrafts(_draftOrders, snapshot);

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
            UpdateProductWorkflowPanel(null);
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
            .Append("  |  群组：").Append(DisplayValue(draft.BusinessGroupName, "未选择"))
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
        UpdateProductWorkflowPanel(draft);
    }

    private void UpdateProductWorkflowPanel(OrderDraft? draft)
    {
        if (draft is null || draft.Items.Count == 0)
        {
            TxtProductWorkflowSummary.Text = draft is null
                ? "商品编码工作流：未选中订单。"
                : "商品编码工作流：当前订单还没有商品。";
            TxtProductWorkflowHint.Text = "解析后会按 周期 / 型号 / 度数 自动尝试直配商品编码。";
            return;
        }

        var exactCount = draft.Items.Count(item => string.Equals(item.ProductMatchState, "Exact", StringComparison.OrdinalIgnoreCase));
        var partialCount = draft.Items.Count(item => string.Equals(item.ProductMatchState, "Partial", StringComparison.OrdinalIgnoreCase));
        var unmatchedCount = draft.Items.Count(item => string.Equals(item.ProductMatchState, "Unmatched", StringComparison.OrdinalIgnoreCase));
        var confirmedCount = draft.Items.Count(item => item.ProductCodeConfirmed);

        TxtProductWorkflowSummary.Text =
            $"商品编码工作流：共 {draft.Items.Count} 项，完全匹配 {exactCount} 项，不完全匹配 {partialCount} 项，未匹配 {unmatchedCount} 项，已确认 {confirmedCount} 项。";

        var nextItem = draft.Items.FirstOrDefault(item => !string.Equals(item.ProductMatchState, "Exact", StringComparison.OrdinalIgnoreCase))
            ?? draft.Items.FirstOrDefault(item => !item.ProductCodeConfirmed);
        TxtProductWorkflowHint.Text = nextItem is null
            ? "当前订单的商品编码都已跑通，可以继续校验并上传。"
            : $"下一步：处理“{DisplayValue(nextItem.ProductConditionSummary, nextItem.SourceText)}”，当前处于 {nextItem.ProductWorkflowSummary}";
    }

    private void UpdateActionAvailability()
    {
        var hasDrafts = _draftOrders.Count > 0;
        var hasSelectedDraft = _selectedDraft is not null;
        var hasRunnableDraft = _draftOrders.Any(item =>
            !string.Equals(item.Status, "上传成功", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(item.Status, "已跳过", StringComparison.OrdinalIgnoreCase));

        BtnParse.IsEnabled = !_isParsing;
        BtnSaveParseRecord.IsEnabled = !_isParsing && hasDrafts;
        BtnValidateCurrent.IsEnabled = !_isParsing && hasSelectedDraft;
        BtnSubmitCurrent.IsEnabled = !_isParsing && hasSelectedDraft;
        BtnSkipCurrent.IsEnabled = !_isParsing && hasSelectedDraft;
        BtnAddItem.IsEnabled = !_isParsing && hasSelectedDraft;
        BtnRemoveItem.IsEnabled = !_isParsing && hasSelectedDraft && GridDraftItems.SelectedItem is OrderItemDraft;
        BtnSubmitAll.IsEnabled = !_isParsing && hasRunnableDraft;
        BtnQueryTradeRecords.IsEnabled = !_isParsing;
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

        var rawKeyword = keyword?.Trim() ?? string.Empty;
        var compactKeyword = MatchTextHelper.Compact(rawKeyword);
        var initialKeyword = Regex.Replace(rawKeyword, @"[^A-Za-z0-9]", string.Empty).ToLowerInvariant();
        var terms = Regex.Split(rawKeyword, @"[\s,，;/；|]+")
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .ToArray();
        var view = CollectionViewSource.GetDefaultView(comboBox.ItemsSource);
        view.Filter = item => item is ProductCodeOption option && MatchesProductCodeOption(option, rawKeyword, compactKeyword, initialKeyword, terms);
        view.Refresh();
    }

    private static string BuildTradeQueryDisplayText(HupunUploadAttemptResult result)
    {
        var summary = ExtractTradeQuerySummary(result.ResponseText);
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(result.FriendlyMessage))
        {
            lines.Add($"message: {result.FriendlyMessage}");
        }

        if (summary.Count >= 0)
        {
            lines.Add($"query_count: {summary.Count}");
        }

        if (summary.TradeNumbers.Count > 0)
        {
            lines.Add($"serial_trade_no(top10): {string.Join(", ", summary.TradeNumbers.Take(10))}");
        }

        if (summary.Uids.Count > 0)
        {
            lines.Add($"serial_uid(top10): {string.Join(", ", summary.Uids.Take(10))}");
        }

        var tradeTable = BuildTradeTable(result.ResponseText, maxRows: 100);
        if (!string.IsNullOrWhiteSpace(tradeTable))
        {
            lines.Add(string.Empty);
            lines.Add("trade_table:");
            lines.Add(tradeTable);
        }

        var orderTable = BuildOrderTable(result.ResponseText, maxRows: 200);
        if (!string.IsNullOrWhiteSpace(orderTable))
        {
            lines.Add(string.Empty);
            lines.Add("order_table(top200):");
            lines.Add(orderTable);
        }

        lines.Add(string.Empty);
        lines.Add(result.DebugText);

        return string.Join(Environment.NewLine, lines);
    }

    private static TradeQuerySummary ExtractTradeQuerySummary(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return new TradeQuerySummary(0, Array.Empty<string>(), Array.Empty<string>());
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            {
                return new TradeQuerySummary(0, Array.Empty<string>(), Array.Empty<string>());
            }

            var tradeNumbers = new List<string>();
            var uids = new List<string>();
            foreach (var row in dataElement.EnumerateArray())
            {
                var tradeNo = ReadJsonText(row, "trade_no");
                if (!string.IsNullOrWhiteSpace(tradeNo))
                {
                    tradeNumbers.Add(tradeNo);
                }

                var uid = ReadJsonText(row, "uid");
                if (!string.IsNullOrWhiteSpace(uid))
                {
                    uids.Add(uid);
                }
            }

            return new TradeQuerySummary(dataElement.GetArrayLength(), tradeNumbers, uids);
        }
        catch
        {
            return new TradeQuerySummary(0, Array.Empty<string>(), Array.Empty<string>());
        }
    }

    private static string BuildTradeTable(string responseText, int maxRows)
    {
        if (!TryGetTradeData(responseText, out var data))
        {
            return string.Empty;
        }

        var rows = new List<IReadOnlyList<string>>();
        foreach (var trade in data.EnumerateArray().Take(Math.Max(1, maxRows)))
        {
            rows.Add(new[]
            {
                ReadJsonText(trade, "trade_no"),
                ReadJsonText(trade, "shop_nick"),
                ReadJsonText(trade, "source_platform"),
                ReadJsonText(trade, "receiver"),
                ReadJsonText(trade, "status"),
                ReadJsonArrayCount(trade, "orders"),
                ReadUnixMillisecondsText(trade, "create_time"),
                ReadUnixMillisecondsText(trade, "end_time")
            });
        }

        if (rows.Count == 0)
        {
            return string.Empty;
        }

        return BuildPlainTextTable(
            new[] { "trade_no", "shop_nick", "platform", "receiver", "status", "orders", "create_time", "end_time" },
            rows,
            maxColumnWidth: 24);
    }

    private static string BuildOrderTable(string responseText, int maxRows)
    {
        if (!TryGetTradeData(responseText, out var data))
        {
            return string.Empty;
        }

        var rows = new List<IReadOnlyList<string>>();
        foreach (var trade in data.EnumerateArray())
        {
            var tradeNo = ReadJsonText(trade, "trade_no");
            if (!trade.TryGetProperty("orders", out var ordersElement) || ordersElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var order in ordersElement.EnumerateArray())
            {
                rows.Add(new[]
                {
                    tradeNo,
                    ReadJsonText(order, "order_id"),
                    ReadJsonText(order, "sku_code"),
                    ReadJsonText(order, "item_name"),
                    ReadJsonText(order, "size"),
                    ReadJsonText(order, "status"),
                    ReadJsonText(order, "bar_code")
                });

                if (rows.Count >= Math.Max(1, maxRows))
                {
                    return BuildPlainTextTable(
                        new[] { "trade_no", "order_id", "sku_code", "item_name", "size", "status", "bar_code" },
                        rows,
                        maxColumnWidth: 26);
                }
            }
        }

        if (rows.Count == 0)
        {
            return string.Empty;
        }

        return BuildPlainTextTable(
            new[] { "trade_no", "order_id", "sku_code", "item_name", "size", "status", "bar_code" },
            rows,
            maxColumnWidth: 26);
    }

    private static bool TryGetTradeData(string responseText, out JsonElement dataElement)
    {
        dataElement = default;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            dataElement = data.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadJsonArrayCount(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return "0";
        }

        return property.GetArrayLength().ToString();
    }

    private static string ReadUnixMillisecondsText(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        long milliseconds;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
        {
            milliseconds = number;
        }
        else if (property.ValueKind == JsonValueKind.String &&
                 long.TryParse(property.GetString(), out var parsed))
        {
            milliseconds = parsed;
        }
        else
        {
            return ReadJsonText(parent, propertyName);
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            return milliseconds.ToString();
        }
    }

    private static string BuildPlainTextTable(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        int maxColumnWidth)
    {
        if (headers.Count == 0)
        {
            return string.Empty;
        }

        var widths = new int[headers.Count];
        for (var i = 0; i < headers.Count; i++)
        {
            widths[i] = Math.Min(maxColumnWidth, NormalizeTableCell(headers[i]).Length);
        }

        foreach (var row in rows)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                var value = i < row.Count ? NormalizeTableCell(row[i]) : string.Empty;
                widths[i] = Math.Min(maxColumnWidth, Math.Max(widths[i], value.Length));
            }
        }

        var lines = new List<string>
        {
            BuildTableLine(headers.Select((header, index) => FitTableCell(header, widths[index])).ToArray()),
            BuildTableLine(widths.Select(width => new string('-', Math.Max(3, width))).ToArray())
        };

        foreach (var row in rows)
        {
            var values = new string[headers.Count];
            for (var i = 0; i < headers.Count; i++)
            {
                var value = i < row.Count ? row[i] : string.Empty;
                values[i] = FitTableCell(value, widths[i]);
            }

            lines.Add(BuildTableLine(values));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildTableLine(IReadOnlyList<string> cells)
    {
        return $"| {string.Join(" | ", cells)} |";
    }

    private static string NormalizeTableCell(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        text = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        while (text.Contains("  ", StringComparison.Ordinal))
        {
            text = text.Replace("  ", " ");
        }

        return text;
    }

    private static string FitTableCell(string? value, int width)
    {
        var text = NormalizeTableCell(value);
        if (text.Length > width)
        {
            return text[..Math.Max(0, width - 3)] + "...";
        }

        return text.PadRight(width, ' ');
    }

    private static string ReadJsonText(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => string.Empty
        };
    }

    private readonly record struct TradeQuerySummary(int Count, IReadOnlyList<string> TradeNumbers, IReadOnlyList<string> Uids);

    private static bool MatchesProductCodeOption(
        ProductCodeOption option,
        string rawKeyword,
        string compactKeyword,
        string initialKeyword,
        IReadOnlyList<string> terms)
    {
        if (string.IsNullOrWhiteSpace(rawKeyword))
        {
            return option.SortOrder < DefaultProductCodeVisibleCount;
        }

        if (terms.Count > 1 && terms.All(term =>
                option.DisplayText.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                option.SearchText.Contains(MatchTextHelper.Compact(term), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(compactKeyword) &&
            option.SearchText.Contains(compactKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(initialKeyword) &&
            option.Initials.Contains(initialKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return option.DisplayText.Contains(rawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.ProductCode.Contains(rawKeyword, StringComparison.OrdinalIgnoreCase) ||
               option.CoreCode.Contains(rawKeyword, StringComparison.OrdinalIgnoreCase);
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

    private sealed record ParseTaskResult(IReadOnlyList<OrderDraft> Drafts, ParseResult ParseResult);
}

public sealed class TrainingOrderDefinition
{
    public string ReceiverName { get; set; } = string.Empty;

    public string ReceiverMobile { get; set; } = string.Empty;

    public string ReceiverAddress { get; set; } = string.Empty;

    public string WearPeriod { get; set; } = string.Empty;

    public List<string> WearAliases { get; set; } = new();

    public string WearAliasesText
    {
        get => TrainingTextHelper.Join(WearAliases);
        set => WearAliases = TrainingTextHelper.Split(value);
    }

    public ObservableCollection<TrainingItemDefinition> Items { get; set; } = new();
}

public sealed class TrainingItemDefinition
{
    public string ProductName { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = new();

    public string AliasesText
    {
        get => TrainingTextHelper.Join(Aliases);
        set => Aliases = TrainingTextHelper.Split(value);
    }

    public string WearPeriod { get; set; } = string.Empty;

    public List<string> WearAliases { get; set; } = new();

    public string WearAliasesText
    {
        get => TrainingTextHelper.Join(WearAliases);
        set => WearAliases = TrainingTextHelper.Split(value);
    }

    public string Degree { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;
}

internal static class TrainingTextHelper
{
    public static string Join(IEnumerable<string>? values)
    {
        return values is null
            ? string.Empty
            : string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
    }

    public static List<string> Split(string? text)
    {
        return Regex.Split(text ?? string.Empty, @"[\r\n,，;；]+")
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
