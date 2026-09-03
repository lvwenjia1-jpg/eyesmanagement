using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading;
using Microsoft.Win32;
using OrderTextTrainer.Core.Models;
using OrderTextTrainer.Core.Services;
using System.Text.RegularExpressions;

namespace WpfApp11;

public partial class MainWindow : Window
{
    private static readonly Brush WorkflowNeutralBrush = CreateFrozenBrush("#334155");
    private static readonly Brush WorkflowMutedBrush = CreateFrozenBrush("#64748B");
    private static readonly Brush WorkflowSuccessBrush = CreateFrozenBrush("#166534");
    private static readonly Brush WorkflowDangerBrush = CreateFrozenBrush("#B91C1C");
    private static readonly Brush WorkflowStockBrush = CreateFrozenBrush("#1D4ED8");
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
    private readonly SemaphoreSlim _historyPersistenceGate = new(1, 1);
    private readonly MainApiSession? _session;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _draftContactEditTimer = new() { Interval = TimeSpan.FromMilliseconds(240) };

    private ParserRuleSet _ruleSet = ParserRuleSet.CreateDefault();
    private ObservableCollection<LookupValueRow> _wearPeriods = new();
    private ObservableCollection<WearPeriodMappingRow> _wearMappings = new();
    private ObservableCollection<ProductCatalogEntry> _productCatalog = new();
    private ObservableCollection<ProductCatalogGroupRow> _productCatalogGroups = new();
    private ObservableCollection<ProductCatalogDegreeRow> _productCatalogDegrees = new();
    private ObservableCollection<ProductCodeMappingRow> _productMappings = new();
    private ObservableCollection<UserAccountRow> _userAccounts = new();
    private ObservableCollection<BusinessGroupOption> _businessGroups = new();
    private ObservableCollection<string> _historyBusinessGroups = new();
    private ObservableCollection<OrderDraft> _draftOrders = new();
    private ObservableCollection<OrderAuditRecord> _historyEntries = new();
    private List<OrderAuditRecord> _allHistoryEntries = new();
    private readonly Dictionary<long, Task<MainApiSyncClient.UploadDetailResult>> _historyDetailLoadTasks = new();
    private int _historyLoadVersion;
    private int _historyPageNumber = 1;
    private int _historyPageSize = 10;
    private int _historyTotalCount;
    private bool _historyRefreshPending;
    private bool _isRefreshingServerData;
    private ObservableCollection<TrainingOrderDefinition> _trainingOrders = new();
    private ParseResult? _lastParseResult;
    private UploadConfiguration _uploadConfiguration = new();
    private MainApiConfiguration _mainApiConfiguration = new();
    private OrderDraft? _selectedDraft;
    private ProductCodePickerWindow? _openProductCodePicker;
    private OrderItemDraft? _openProductCodePickerItem;
    private bool _isApplyingLoggedInAccount;
    private bool _isParsing;
    private bool _isLoadingDraftForm;
    private readonly List<int> _sourceSearchMatchIndexes = new();
    private string _sourceSearchKeyword = string.Empty;
    private string _sourceSearchSourceText = string.Empty;
    private int _sourceSearchCurrentIndex = -1;
    private int _sourceSearchMatchLength;
    private const int ParseDraftBatchSize = 1;
    private const string ProductCodeComboSuppressToken = "__product-code-suppress__";
    private const string AllHistoryBusinessGroupOption = "全部";
    private const string ManagerRole = "manager";
    private const string TradeRecordQueryBillCode = "3250136715120821388";
    private static readonly bool DisableCatalogJsonSyncTemporarily = false;
    private static readonly bool DisableWearSettingsSyncTemporarily = false;
    private static readonly bool DisableHistorySyncTemporarily = false;
    private DateTime _lastServerSyncCheckUtc = DateTime.MinValue;

    public MainWindow(MainApiSession? session = null)
    {
        _session = session;
        InitializeComponent();
        SetHistoryBusinessGroupOptions(Array.Empty<string>(), null);
        _toastTimer.Tick += ToastTimer_Tick;
        _draftContactEditTimer.Tick += DraftContactEditTimer_Tick;
        LoadSettingsIntoUi(_settingsRepository.LoadOrCreate());
        LoadHistory();
        MainTabs.SelectionChanged += MainTabs_SelectionChanged;
        Loaded += MainWindow_Loaded;

        TxtInput.Text = string.Empty;
        TxtParseSummary.Text = "把订单文本贴到左侧；可按单订单识别，或开启批量识别后拆成多条订单草稿。";
        TxtQueueSummary.Text = "当前还没有解析结果。";
        TxtValidationOutput.Text = "待校验。";
        TxtUploadOutput.Text = "待上传。";
        TxtTradeQueryResult.Text = "订单查询结果会显示在这里。";
        TxtCurrentRawOrder.Text = "尚未选择订单。";
        TxtCurrentDraftHeadline.Text = "尚未选择订单";
        TxtCurrentDraftMeta.Text = "先解析文本，再从队列中选择一条订单开始审核。";
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
        UpdateLastRefreshTimeText();
        UpdateWorkbenchState();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLoggedInAccount();
        await LoadBusinessGroupsAsync();
        await SyncCatalogFromServerAsync(showStatus: true);
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs) || !IsHistoryTabActive() || !_historyRefreshPending)
        {
            return;
        }

        _historyRefreshPending = false;
        LoadHistory(preserveSelection: true);
    }

    private void ToastTimer_Tick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        ToastHost.Visibility = Visibility.Collapsed;
    }

    private bool IsHistoryTabActive()
    {
        return MainTabs.SelectedItem is TabItem { Header: string header } &&
               string.Equals(header, "历史记录", StringComparison.Ordinal);
    }

    private string GetCurrentHistoryOperatorLoginName()
    {
        var loginName = _session?.User.LoginName?.Trim();
        if (!string.IsNullOrWhiteSpace(loginName))
        {
            return loginName;
        }

        return (CmbOperatorAccounts.SelectedItem as UserAccountRow)?.LoginName?.Trim() ?? string.Empty;
    }

    private bool IsHistoryAdministrator()
    {
        var role = _session?.User.Role?.Trim();
        return string.Equals(role, ManagerRole, StringComparison.OrdinalIgnoreCase);
    }

    private string GetHistoryUploaderLoginName()
    {
        return IsHistoryAdministrator() ? string.Empty : GetCurrentHistoryOperatorLoginName();
    }

    private string GetDefaultHistoryOperatorFilterValue()
    {
        return IsHistoryAdministrator() ? string.Empty : GetCurrentHistoryOperatorLoginName();
    }

    private bool IsHistoryEntryVisibleToCurrentUser(OrderAuditRecord entry)
    {
        return IsHistoryAdministrator() || IsHistoryEntryOwnedByCurrentUser(entry);
    }

    private bool IsHistoryEntryOwnedByCurrentUser(OrderAuditRecord entry)
    {
        var currentLoginName = GetCurrentHistoryOperatorLoginName();
        return !string.IsNullOrWhiteSpace(currentLoginName) &&
               !string.IsNullOrWhiteSpace(entry.OperatorLoginName) &&
               string.Equals(entry.OperatorLoginName.Trim(), currentLoginName, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanCurrentUserCancelHistoryEntry(OrderAuditRecord entry)
    {
        if (string.Equals(NormalizeHistoryStatus(entry.Status), "已取消", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsHistoryAdministrator())
        {
            return true;
        }

        var currentLoginName = GetCurrentHistoryOperatorLoginName();
        if (string.IsNullOrWhiteSpace(currentLoginName) || string.IsNullOrWhiteSpace(entry.OperatorLoginName))
        {
            return false;
        }

        return string.Equals(entry.OperatorLoginName.Trim(), currentLoginName, StringComparison.OrdinalIgnoreCase);
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
        }
        finally
        {
            _isApplyingLoggedInAccount = false;
        }

        ApplyHistoryEntryCapabilities(_allHistoryEntries);
        GridHistory.Items.Refresh();
        TxtHistoryOperatorFilter.IsReadOnly = !IsHistoryAdministrator();
        TxtHistoryOperatorFilter.Text = GetDefaultHistoryOperatorFilterValue();
        TxtStatus.Text = $"当前登录账号：{currentUser.LoginName}";
    }

    private async Task LoadBusinessGroupsAsync(bool preserveSelection = true, bool rethrowOnError = false)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            var selectedBusinessGroupId = preserveSelection
                ? (CmbBusinessGroups.SelectedItem as BusinessGroupOption)?.Id
                : null;
            var groups = await _mainApiSyncClient.QueryBusinessGroupsAsync(_session.Configuration);
            _businessGroups = new ObservableCollection<BusinessGroupOption>(groups.Select(item => new BusinessGroupOption
            {
                Id = item.Id,
                Name = item.Name
            }));
            CmbBusinessGroups.ItemsSource = _businessGroups;
            if (selectedBusinessGroupId.HasValue)
            {
                var matchedGroup = _businessGroups.FirstOrDefault(item => item.Id == selectedBusinessGroupId.Value);
                if (matchedGroup is not null)
                {
                    CmbBusinessGroups.SelectedItem = matchedGroup;
                }
            }

            if (_businessGroups.Count > 0 && CmbBusinessGroups.SelectedItem is null)
            {
                CmbBusinessGroups.SelectedIndex = 0;
            }

            await LoadHistoryBusinessGroupsAsync();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"业务群加载失败：{ex.Message}";
            if (rethrowOnError)
            {
                throw;
            }
        }
    }

    private async Task LoadHistoryBusinessGroupsAsync()
    {
        var uploaderLoginName = GetHistoryUploaderLoginName();
        if ((!IsHistoryAdministrator() && string.IsNullOrWhiteSpace(uploaderLoginName)) || !_mainApiConfiguration.IsEnabled)
        {
            return;
        }

        var selectedBusinessGroupName = GetSelectedHistoryBusinessGroupName();
            var businessGroupNames = await _mainApiSyncClient.QueryUploadBusinessGroupNamesAsync(
                _mainApiConfiguration,
                uploaderLoginName);
        SetHistoryBusinessGroupOptions(businessGroupNames, selectedBusinessGroupName);
    }

    private void SetHistoryBusinessGroupOptions(
        IEnumerable<string> businessGroupNames,
        string? selectedBusinessGroupName)
    {
        _historyBusinessGroups = new ObservableCollection<string>(businessGroupNames
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Where(item => !string.Equals(item, AllHistoryBusinessGroupOption, StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Prepend(AllHistoryBusinessGroupOption));
        CmbHistoryBusinessGroupFilter.ItemsSource = _historyBusinessGroups;

        if (!string.IsNullOrWhiteSpace(selectedBusinessGroupName))
        {
            var matchedBusinessGroupName = _historyBusinessGroups.FirstOrDefault(item =>
                string.Equals(item, selectedBusinessGroupName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matchedBusinessGroupName))
            {
                CmbHistoryBusinessGroupFilter.SelectedItem = matchedBusinessGroupName;
                return;
            }
        }

        CmbHistoryBusinessGroupFilter.SelectedItem = AllHistoryBusinessGroupOption;
    }

    private string GetSelectedHistoryBusinessGroupName()
    {
        var businessGroupName = (CmbHistoryBusinessGroupFilter.SelectedItem as string)?.Trim() ?? string.Empty;
        return string.Equals(businessGroupName, AllHistoryBusinessGroupOption, StringComparison.Ordinal)
            ? string.Empty
            : businessGroupName;
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
        ResetSourceSearchState();
        TxtStatus.Text = "正在后台解析订单文本，请稍候…";
        TxtParseSummary.Text = "正在解析订单文本…";
        _draftOrders.Clear();
        _selectedDraft = null;
        LoadDraftToForm(null);
        GridDraftOrders.Items.Refresh();
        UpdateActionAvailability();

        SyncProgressWindow? parseWindow = null;
        Exception? parseException = null;

        try
        {
            await EnsureLatestServerSettingsBeforeParseAsync();
            snapshot = BuildSnapshotFromUi();
            var rawText = TxtInput.Text;
            var batchRecognizeEnabled = ChkBatchRecognize.IsChecked == true;
            parseWindow = new SyncProgressWindow("正在解析订单文本，请稍候…","正在解析")
            {
                Owner = this
            };
            IsEnabled = false;
            parseWindow.Show();
            var parseTaskResult = await Task.Run(() =>
            {
                var pendingUiTasks = new List<Task>();
                var resolverSession = _catalogSkuResolver.CreateSession(snapshot);
                IReadOnlyList<OrderDraft> drafts;
                ParseResult parseResult;

                if (batchRecognizeEnabled)
                {
                    drafts = _draftFactory.CreateDraftsInBatches(
                        rawText,
                        snapshot,
                        selectedAccountSnapshot,
                        ParseDraftBatchSize,
                        batch =>
                        {
                            var batchCopy = batch.ToList();
                            _catalogSkuResolver.RefreshDrafts(batchCopy, snapshot, resolverSession);
                            var appendTask = Dispatcher
                                .InvokeAsync(() => AppendDraftBatch(batchCopy), DispatcherPriority.Background)
                                .Task;
                            lock (pendingUiTasks)
                            {
                                pendingUiTasks.Add(appendTask);
                            }
                        },
                        out parseResult,
                        allowMultipleOrders: true);
                }
                else
                {
                    drafts = _draftFactory.CreateDrafts(
                        rawText,
                        snapshot,
                        selectedAccountSnapshot,
                        out parseResult,
                        allowMultipleOrders: false);

                    var draftBatch = drafts.ToList();
                    _catalogSkuResolver.RefreshDrafts(draftBatch, snapshot, resolverSession);
                    var appendTask = Dispatcher
                        .InvokeAsync(() => AppendDraftBatch(draftBatch), DispatcherPriority.Background)
                        .Task;
                    lock (pendingUiTasks)
                    {
                        pendingUiTasks.Add(appendTask);
                    }
                }

                return new ParseTaskResult(drafts, parseResult, pendingUiTasks.ToArray());
            });

            await Task.WhenAll(parseTaskResult.PendingUiTasks);
            _lastParseResult = parseTaskResult.ParseResult;
            ApplySelectedBusinessGroupToDrafts();
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
            parseException = ex;
            TxtStatus.Text = $"解析失败：{ex.Message}";
            TxtValidationOutput.Text = ex.Message;
        }
        finally
        {
            parseWindow?.Close();
            IsEnabled = true;
            _isParsing = false;
            UpdateActionAvailability();
            Activate();
        }

        if (parseException is not null)
        {
            MessageBox.Show(this, $"解析失败：{parseException.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

        var isSuccess = await UploadDraftAsync(_selectedDraft, moveToNext: true);
        if (isSuccess)
        {
            await RefreshHistoryAfterUploadAsync();
        }
    }

    private async void BtnSubmitAll_Click(object sender, RoutedEventArgs e)
    {
        PersistSelectedDraftFromForm();
        if (_draftOrders.Count == 0)
        {
            MessageBox.Show("当前没有待上传订单。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedDrafts = _draftOrders
            .Where(item => item.IsBatchUploadSelected)
            .Where(item =>
                !string.Equals(item.Status, "上传成功", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Status, "已跳过", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (selectedDrafts.Count == 0)
        {
            MessageBox.Show("请先勾选要批量上传的订单。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateActionAvailability();
            return;
        }

        BtnSubmitAll.IsEnabled = false;
        var hasSuccessfulUpload = false;
        try
        {
            foreach (var draft in selectedDrafts)
            {
                GridDraftOrders.SelectedItem = draft;
                var isSuccess = await UploadDraftAsync(draft, moveToNext: false);
                if (!isSuccess)
                {
                    if (hasSuccessfulUpload)
                    {
                        await RefreshHistoryAfterUploadAsync();
                    }

                    TxtStatus.Text = $"批量上传已中断，请先处理订单 {DisplayValue(draft.OrderNumber, draft.DraftId)} 的失败原因。";
                    GridDraftOrders.SelectedItem = draft;
                    GridDraftOrders.ScrollIntoView(draft);
                    return;
                }

                hasSuccessfulUpload = true;
            }

            MoveToNextDraft();
            await RefreshHistoryAfterUploadAsync();
        }
        finally
        {
            UpdateActionAvailability();
        }
    }

    private void BatchUploadSelectionCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateActionAvailability();
    }

    private async void BtnQueryTradeRecords_Click(object sender, RoutedEventArgs e)
    {
        PersistSelectedDraftFromForm();
        BtnQueryTradeRecords.IsEnabled = false;
        try
        {
            var snapshot = BuildSnapshotFromUi();
            var queryDraft = new OrderDraft
            {
                OrderNumber = TradeRecordQueryBillCode
            };
            var result = await _tradeUploader.QueryTradeListAsync(queryDraft, snapshot.Upload);
            var displayText = BuildTradeQueryDisplayText(result);
            TxtTradeQueryResult.Text = displayText;
            TxtUploadOutput.Text = displayText;
            TxtStatus.Text = result.IsSuccess
                ? $"订单记录查询完成（bill_code={TradeRecordQueryBillCode}）。"
                : $"订单记录查询已返回（bill_code={TradeRecordQueryBillCode}），请检查接口结果。";
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

    private async void BtnQueryGoodsCodes_Click(object sender, RoutedEventArgs e)
    {
        BtnQueryGoodsCodes.IsEnabled = false;
        try
        {
            var snapshot = BuildSnapshotFromUi();
            var result = await _tradeUploader.QueryAllGoodsWithSpecListAsync(snapshot.Upload, limit: 200);
            var displayText = BuildFullGoodsCodeQueryDisplayText(result);
            TxtTradeQueryResult.Text = displayText;
            TxtUploadOutput.Text = displayText;
            TxtStatus.Text = result.IsSuccess ? "商品编码全量查询完成。" : "商品编码查询已返回，请检查接口结果。";
        }
        catch (Exception ex)
        {
            TxtTradeQueryResult.Text = ex.ToString();
            TxtUploadOutput.Text = ex.ToString();
            TxtStatus.Text = "商品编码查询失败。";
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

    private async void BtnImportCatalog_Click(object sender, RoutedEventArgs e)
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

            if (!DisableCatalogJsonSyncTemporarily && _session is not null && _session.Configuration.IsEnabled)
            {
                TxtStatus.Text = "正在把商品目录增量同步到服务器...";
                var importResult = await _mainApiSyncClient.ImportProductCatalogAsync(
                    entries.ToList(),
                    string.Join("; ", selectedFiles.Select(Path.GetFileName)),
                    _session.Configuration);
                await SyncCatalogFromServerAsync(showStatus: false);
                TxtStatus.Text = $"商品目录已同步：新增 {importResult.AddedCount} 条，更新 {importResult.UpdatedCount} 条，跳过 {importResult.SkippedCount} 条。";
                TxtSettingsStatus.Text = "商品目录已按增量方式同步到服务器，并回拉到本地。";
                UpdateWorkbenchState();
                return;
            }

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
            var message = $"导入商品列表失败：{ex.Message}";
            TxtStatus.Text = message;
            TxtSettingsStatus.Text = message;
            MessageBox.Show(this, message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SyncCatalogFromServerAsync(bool showStatus, bool rethrowOnError = false)
    {
        if (DisableCatalogJsonSyncTemporarily)
        {
            if (showStatus)
            {
                TxtStatus.Text = "已临时禁用商品编码服务器同步，当前继续使用本地数据。";
                TxtSettingsStatus.Text = "商品编码服务器同步已临时关闭。";
            }

            return;
        }

        if (_session is null || !_session.Configuration.IsEnabled)
        {
            return;
        }

        try
        {
            var serverCatalog = await _mainApiSyncClient.ListProductCatalogAsync(_session.Configuration);
            if (!DisableWearSettingsSyncTemporarily)
            {
                var wearSettings = await _mainApiSyncClient.GetWearPeriodSettingsAsync(_session.Configuration);
                _wearPeriods = new ObservableCollection<LookupValueRow>(
                    wearSettings.WearPeriods.Select(item => new LookupValueRow
                    {
                        Value = item
                    }));
                _wearMappings = new ObservableCollection<WearPeriodMappingRow>(
                    wearSettings.WearPeriodMappings.Select(item => new WearPeriodMappingRow
                    {
                        Alias = item.Alias,
                        WearPeriod = item.WearPeriod
                    }));

                GridWearPeriods.ItemsSource = _wearPeriods;
                GridWearMappings.ItemsSource = _wearMappings;
            }

            _productCatalog = new ObservableCollection<ProductCatalogEntry>(
                serverCatalog.Select(item => new ProductCatalogEntry
                {
                    ProductCode = item.ProductCode,
                    ProductName = item.ProductName,
                    SpecCode = item.SpecCode,
                    Barcode = item.Barcode,
                    BaseName = item.BaseName,
                    SpecificationToken = item.SpecificationToken,
                    ModelToken = item.ModelToken,
                    Degree = item.Degree,
                    SearchText = item.SearchText,
                    IsOutOfStock = item.IsOutOfStock
                }));

            RebuildProductCatalogView();
            var snapshot = BuildSnapshotFromUi();
            _settingsRepository.Save(snapshot);
            RefreshLookupSources();
            RefreshAllDraftResolutions();
            UpdateWorkbenchState();

            if (showStatus)
            {
                if (DisableWearSettingsSyncTemporarily)
                {
                    TxtStatus.Text = $"已从服务器同步商品目录，共 {_productCatalog.Count} 条商品；周期设置继续使用本地配置。";
                    TxtSettingsStatus.Text = "商品目录已同步为服务器最新版本，周期设置仍使用本地配置。";
                }
                else
                {
                    TxtStatus.Text = $"已从服务器同步周期设置和商品目录，共 {_productCatalog.Count} 条商品。";
                    TxtSettingsStatus.Text = "周期设置与商品目录已同步为服务器最新版本。";
                }
            }
        }
        catch (Exception ex)
        {
            if (showStatus)
            {
                TxtStatus.Text = $"服务器同步失败，继续使用本地数据：{ex.Message}";
            }

            if (rethrowOnError)
            {
                throw;
            }
        }
    }

    private async Task EnsureLatestServerSettingsBeforeParseAsync()
    {
        if (_session is null || !_session.Configuration.IsEnabled)
        {
            return;
        }

        if (DateTime.UtcNow - _lastServerSyncCheckUtc < TimeSpan.FromSeconds(3))
        {
            return;
        }

        _lastServerSyncCheckUtc = DateTime.UtcNow;
        var syncWindow = new SyncProgressWindow("同步服务器商品编码中，请稍候…") { Owner = this };
        IsEnabled = false;
        syncWindow.Show();

        try
        {
            var shouldSyncCatalog = false;
            var syncedWearSettings = false;

            if (!DisableWearSettingsSyncTemporarily)
            {
                var wearSettings = await _mainApiSyncClient.GetWearPeriodSettingsAsync(_session.Configuration);
                ApplyWearSettingsToUi(wearSettings);
                syncedWearSettings = true;
            }

            if (!DisableCatalogJsonSyncTemporarily)
            {
                var serverUpdatedAtUtc = await _mainApiSyncClient.GetProductCatalogLastUpdatedAtUtcAsync(_session.Configuration);
                var localUpdatedAtUtc = GetLocalProductCatalogLastWriteUtc();
                shouldSyncCatalog = serverUpdatedAtUtc.HasValue &&
                                    (!localUpdatedAtUtc.HasValue || serverUpdatedAtUtc.Value > localUpdatedAtUtc.Value);

                if (shouldSyncCatalog)
                {
                    var serverCatalog = await _mainApiSyncClient.ListProductCatalogAsync(_session.Configuration);
                    _productCatalog = new ObservableCollection<ProductCatalogEntry>(
                        serverCatalog.Select(item => new ProductCatalogEntry
                        {
                            ProductCode = item.ProductCode,
                            ProductName = item.ProductName,
                            SpecCode = item.SpecCode,
                            Barcode = item.Barcode,
                            BaseName = item.BaseName,
                            SpecificationToken = item.SpecificationToken,
                            ModelToken = item.ModelToken,
                            Degree = item.Degree,
                            SearchText = item.SearchText,
                            IsOutOfStock = item.IsOutOfStock
                        }));

                    RebuildProductCatalogView();
                }
            }

            if (syncedWearSettings || shouldSyncCatalog)
            {
                var refreshedSnapshot = BuildSnapshotFromUi();
                _settingsRepository.Save(refreshedSnapshot);
                RefreshLookupSources();
                RefreshAllDraftResolutions();
                UpdateWorkbenchState();
            }

            if (shouldSyncCatalog)
            {
                TxtStatus.Text = "已同步服务器最新商品编码和缺货状态，继续解析。";
            }
            else if (syncedWearSettings)
            {
                TxtStatus.Text = "已同步服务器最新周期设置，继续解析。";
            }
        }
        finally
        {
            syncWindow.Close();
            IsEnabled = true;
            Activate();
        }
    }

    private void ApplyWearSettingsToUi(MainApiSyncClient.WearPeriodSettingsResult wearSettings)
    {
        _wearPeriods = new ObservableCollection<LookupValueRow>(
            wearSettings.WearPeriods.Select(item => new LookupValueRow
            {
                Value = item
            }));
        _wearMappings = new ObservableCollection<WearPeriodMappingRow>(
            wearSettings.WearPeriodMappings.Select(item => new WearPeriodMappingRow
            {
                Alias = item.Alias,
                WearPeriod = item.WearPeriod
            }));

        GridWearPeriods.ItemsSource = _wearPeriods;
        GridWearMappings.ItemsSource = _wearMappings;
    }

    private static DateTime? GetLocalProductCatalogLastWriteUtc()
    {
        var productCatalogPath = new ProductCatalogRepository().GetDefaultCatalogPath();
        if (!File.Exists(productCatalogPath))
        {
            return null;
        }

        return File.GetLastWriteTimeUtc(productCatalogPath);
    }

    private void BtnSaveParseRecord_Click(object sender, RoutedEventArgs e)
    {
        TxtInput.Clear();
        ResetSourceSearchState();
        UpdateActionAvailability();
        TxtInput.Focus();
        TxtStatus.Text = "已清空当前订单文本源。";
    }

    private void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        if (!TryPrepareSourceSearchFromInput())
        {
            UpdateActionAvailability();
            return;
        }

        _sourceSearchCurrentIndex = 0;
        FocusSourceSearchMatch();
        UpdateActionAvailability();
    }

    private void BtnSearchPrev_Click(object sender, RoutedEventArgs e)
    {
        if (!TryPrepareSourceSearchFromInput())
        {
            UpdateActionAvailability();
            return;
        }

        var count = _sourceSearchMatchIndexes.Count;
        if (count == 0)
        {
            UpdateActionAvailability();
            return;
        }

        var baseIndex = _sourceSearchCurrentIndex < 0 ? 0 : _sourceSearchCurrentIndex;
        _sourceSearchCurrentIndex = (baseIndex - 1 + count) % count;
        FocusSourceSearchMatch();
        UpdateActionAvailability();
    }

    private void BtnSearchNext_Click(object sender, RoutedEventArgs e)
    {
        if (!TryPrepareSourceSearchFromInput())
        {
            UpdateActionAvailability();
            return;
        }

        var count = _sourceSearchMatchIndexes.Count;
        if (count == 0)
        {
            UpdateActionAvailability();
            return;
        }

        var baseIndex = _sourceSearchCurrentIndex < 0 ? -1 : _sourceSearchCurrentIndex;
        _sourceSearchCurrentIndex = (baseIndex + 1 + count) % count;
        FocusSourceSearchMatch();
        UpdateActionAvailability();
    }

    private bool TryPrepareSourceSearchFromInput()
    {
        if (string.IsNullOrWhiteSpace(TxtInput.Text))
        {
            TxtStatus.Text = "请先输入订单文本源内容。";
            return false;
        }

        var keyword = (TxtSearchKeyword.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            TxtStatus.Text = "请先输入查询内容。";
            return false;
        }

        if (TryBuildSourceSearchMatches(keyword))
        {
            return true;
        }

        TxtStatus.Text = "在订单文本源中未找到查询内容。";
        return false;
    }

    private bool TryBuildSourceSearchMatches(string keyword)
    {
        var sourceText = TxtInput.Text ?? string.Empty;
        if (_sourceSearchMatchIndexes.Count > 0 &&
            string.Equals(_sourceSearchSourceText, sourceText, StringComparison.Ordinal) &&
            string.Equals(_sourceSearchKeyword, keyword, StringComparison.Ordinal))
        {
            return true;
        }

        ResetSourceSearchState();
        _sourceSearchSourceText = sourceText;
        _sourceSearchKeyword = keyword;
        _sourceSearchMatchLength = keyword.Length;
        if (_sourceSearchMatchLength == 0)
        {
            return false;
        }

        var scanStart = 0;
        while (scanStart < sourceText.Length)
        {
            var index = sourceText.IndexOf(keyword, scanStart, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            _sourceSearchMatchIndexes.Add(index);
            scanStart = index + _sourceSearchMatchLength;
        }

        return _sourceSearchMatchIndexes.Count > 0;
    }

    private void FocusSourceSearchMatch()
    {
        if (_sourceSearchCurrentIndex < 0 || _sourceSearchCurrentIndex >= _sourceSearchMatchIndexes.Count)
        {
            return;
        }

        var start = _sourceSearchMatchIndexes[_sourceSearchCurrentIndex];
        TxtInput.Focus();
        TxtInput.Select(start, _sourceSearchMatchLength);

        var lineIndex = TxtInput.GetLineIndexFromCharacterIndex(start);
        if (lineIndex >= 0)
        {
            TxtInput.ScrollToLine(lineIndex);
        }

        TxtStatus.Text = $"已定位到第 {_sourceSearchCurrentIndex + 1}/{_sourceSearchMatchIndexes.Count} 处匹配。";
    }

    private void ResetSourceSearchState()
    {
        _sourceSearchMatchIndexes.Clear();
        _sourceSearchKeyword = string.Empty;
        _sourceSearchSourceText = string.Empty;
        _sourceSearchCurrentIndex = -1;
        _sourceSearchMatchLength = 0;
    }

    private void TxtInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        ResetSourceSearchState();
        UpdateActionAvailability();
    }

    private void TxtSearchKeyword_TextChanged(object sender, TextChangedEventArgs e)
    {
        ResetSourceSearchState();
        UpdateActionAvailability();
    }

    private void TxtSearchKeyword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (!BtnSearch.IsEnabled)
        {
            return;
        }

        BtnSearch_Click(BtnSearch, new RoutedEventArgs());
    }

    private void DraftContactField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingDraftForm || _selectedDraft is null)
        {
            return;
        }

        ApplyDraftContactFieldsToSelectedDraft(refreshResolution: false);
        _draftContactEditTimer.Stop();
        _draftContactEditTimer.Start();
    }

    private void DraftContactField_CheckChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingDraftForm || _selectedDraft is null)
        {
            return;
        }

        ApplyDraftContactFieldsToSelectedDraft(refreshResolution: false);
        _draftContactEditTimer.Stop();
        _draftContactEditTimer.Start();
    }

    private void DraftContactEditTimer_Tick(object? sender, EventArgs e)
    {
        _draftContactEditTimer.Stop();
        if (_isLoadingDraftForm || _selectedDraft is null)
        {
            return;
        }

        ApplyDraftContactFieldsToSelectedDraft(refreshResolution: false);
        GridDraftOrders.Items.Refresh();
        UpdateWorkbenchState();
    }

    private void ApplyDraftContactFieldsToSelectedDraft(bool refreshResolution)
    {
        if (_selectedDraft is null)
        {
            return;
        }

        var receiverName = TxtDraftReceiverName.Text.Trim();
        var receiverMobile = TxtDraftReceiverMobile.Text.Trim();
        var receiverAddress = AddressParsingHelper.NormalizeAddressInput(TxtDraftReceiverAddress.Text);
        var remark = TxtDraftRemark.Text.Trim();
        var hasGift = ChkDraftHasGift.IsChecked == true;

        _selectedDraft.ReceiverName = receiverName;
        _selectedDraft.ReceiverMobile = receiverMobile;
        _selectedDraft.ReceiverAddress = receiverAddress;
        _selectedDraft.Remark = remark;
        _selectedDraft.HasGift = hasGift;

        if (refreshResolution)
        {
            RefreshDraftResolution(_selectedDraft);
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
            MatchHint = "手工新增商品。",
            UseManualProductCodeStyle = true
        });

        RenumberDraftItems(_selectedDraft);
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
        RenumberDraftItems(_selectedDraft);
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
        ResetSourceSearchState();
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
            GridDraftOrders.Items.Refresh();
        }

        TxtStatus.Text = $"当前账号：{selectedAccount}";
        ApplyHistoryEntryCapabilities(_allHistoryEntries);
        GridHistory.Items.Refresh();
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

    private async void BtnRefreshServerData_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllServerDataAsync();
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
            GridDraftOrders.Items.Refresh();
            UpdateWorkbenchState();
        }), DispatcherPriority.ContextIdle);
    }

    private void ProductCodeCellButton_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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
            item.IsOutOfStock = args.SelectedOption.IsOutOfStock;
            item.ProductCodeSearchKeyword = args.ConfirmedKeyword;
            item.ProductCodeConfirmed = true;
            item.UseManualProductCodeStyle = true;
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
        e.Handled = true;
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

    private async void GridHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridHistory.SelectedItem is not OrderAuditRecord entry)
        {
            TxtHistoryRaw.Text = "请选择一条历史记录。";
            TxtHistorySnapshot.Text = "请选择一条历史记录。";
            TxtHistoryResponse.Text = "请选择一条历史记录。";
            return;
        }

        if (entry.ServerUploadId.HasValue &&
            !entry.IsServerDetailLoaded &&
            _mainApiConfiguration.IsEnabled)
        {
            TxtHistoryRaw.Text = "正在加载原始文本…";
            TxtHistorySnapshot.Text = "正在加载录入快照…";
            TxtHistoryResponse.Text = "正在加载接口响应…";

            try
            {
                await EnsureHistoryEntryDetailsLoadedAsync(entry);
                if (!ReferenceEquals(GridHistory.SelectedItem, entry))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                if (!ReferenceEquals(GridHistory.SelectedItem, entry))
                {
                    return;
                }

                TxtHistoryRaw.Text = "原始文本加载失败。";
                TxtHistorySnapshot.Text = "录入快照加载失败。";
                TxtHistoryResponse.Text = $"接口响应加载失败：{ex.Message}";
                TxtStatus.Text = $"历史记录详情加载失败：{ex.Message}";
                return;
            }
        }

        TxtHistoryRaw.Text = entry.RawText;
        TxtHistorySnapshot.Text = entry.SnapshotJson;
        TxtHistoryResponse.Text = entry.ResponseText;
    }

    private async Task EnsureHistoryEntryDetailsLoadedAsync(OrderAuditRecord entry)
    {
        if (!entry.ServerUploadId.HasValue ||
            entry.IsServerDetailLoaded ||
            !_mainApiConfiguration.IsEnabled)
        {
            return;
        }

        var uploadId = entry.ServerUploadId.Value;
        if (!_historyDetailLoadTasks.TryGetValue(uploadId, out var detailLoadTask))
        {
            detailLoadTask = _mainApiSyncClient.GetUploadByIdAsync(_mainApiConfiguration, uploadId);
            _historyDetailLoadTasks[uploadId] = detailLoadTask;
        }

        try
        {
            var detail = await detailLoadTask;
            ApplyServerHistoryDetail(entry, detail);
        }
        finally
        {
            _historyDetailLoadTasks.Remove(uploadId);
        }
    }

    private void HistoryFilterInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        _historyPageNumber = 1;
        LoadHistory();
        e.Handled = true;
    }

    private void BtnApplyHistoryFilter_Click(object sender, RoutedEventArgs e)
    {
        _historyPageNumber = 1;
        LoadHistory();
    }

    private void BtnResetHistoryFilter_Click(object sender, RoutedEventArgs e)
    {
        TxtHistoryOrderNumberFilter.Clear();
        TxtHistoryReceiverFilter.Clear();
        TxtHistoryOperatorFilter.Text = GetDefaultHistoryOperatorFilterValue();
        CmbHistoryBusinessGroupFilter.SelectedItem = AllHistoryBusinessGroupOption;
        DpHistoryStartDate.SelectedDate = null;
        DpHistoryEndDate.SelectedDate = null;
        _historyPageNumber = 1;
        LoadHistory(preserveSelection: false);
    }

    private void CmbHistoryPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || CmbHistoryPageSize.SelectedItem is not ComboBoxItem { Content: string content } ||
            !int.TryParse(content, out var pageSize))
        {
            return;
        }

        _historyPageSize = pageSize;
        _historyPageNumber = 1;
        LoadHistory(preserveSelection: false);
    }

    private void CmbHistoryBusinessGroupFilter_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        if (!comboBox.IsDropDownOpen)
        {
            e.Handled = true;
            return;
        }

        var scrollViewer = FindVisualParent<ScrollViewer>(e.OriginalSource as DependencyObject);
        if (scrollViewer is not null)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        }

        e.Handled = true;
    }

    private void BtnHistoryPreviousPage_Click(object sender, RoutedEventArgs e)
    {
        if (_historyPageNumber <= 1)
        {
            return;
        }

        _historyPageNumber--;
        LoadHistory(preserveSelection: false);
    }

    private void BtnHistoryNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_historyPageNumber >= GetHistoryPageCount())
        {
            return;
        }

        _historyPageNumber++;
        LoadHistory(preserveSelection: false);
    }

    private async void BtnCancelHistoryOrder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OrderAuditRecord entry })
        {
            return;
        }

        GridHistory.SelectedItem = entry;
        if (!CanCurrentUserCancelHistoryEntry(entry))
        {
            const string message = "只有业务员为当前登录用户本人上传的订单，才允许取消。";
            TxtStatus.Text = message;
            TxtHistoryResponse.Text = message;
            ShowUploadBlockingDialog("取消订单失败", message);
            SelectHistoryEntry(entry.RecordId);
            return;
        }

        await CancelHistoryOrderAsync(entry);
    }

    private void PersistSelectedDraftFromForm()
    {
        if (_selectedDraft is null)
        {
            return;
        }

        var receiverName = TxtDraftReceiverName.Text.Trim();
        var receiverMobile = TxtDraftReceiverMobile.Text.Trim();
        var receiverAddress = AddressParsingHelper.NormalizeAddressInput(TxtDraftReceiverAddress.Text);
        var remark = TxtDraftRemark.Text.Trim();
        var hasGift = ChkDraftHasGift.IsChecked == true;
        var selectedAccount = GetCurrentLoggedInAccount();
        var selectedGroup = CmbBusinessGroups.SelectedItem as BusinessGroupOption;

        var isUnchanged =
            string.Equals(_selectedDraft.ReceiverName, receiverName, StringComparison.Ordinal) &&
            string.Equals(_selectedDraft.ReceiverMobile, receiverMobile, StringComparison.Ordinal) &&
            string.Equals(_selectedDraft.ReceiverAddress, receiverAddress, StringComparison.Ordinal) &&
            string.Equals(_selectedDraft.Remark, remark, StringComparison.Ordinal) &&
            _selectedDraft.HasGift == hasGift &&
            (selectedAccount is null ||
             (string.Equals(_selectedDraft.OperatorLoginName, selectedAccount.LoginName, StringComparison.Ordinal) &&
              string.Equals(_selectedDraft.OperatorErpId, selectedAccount.ErpId, StringComparison.Ordinal))) &&
            ((selectedGroup is null &&
              !_selectedDraft.BusinessGroupId.HasValue &&
              string.IsNullOrWhiteSpace(_selectedDraft.BusinessGroupName)) ||
             (selectedGroup is not null &&
              _selectedDraft.BusinessGroupId == selectedGroup.Id &&
              string.Equals(_selectedDraft.BusinessGroupName, selectedGroup.Name, StringComparison.Ordinal)));

        if (isUnchanged)
        {
            return;
        }

        ApplyDraftContactFieldsToSelectedDraft(refreshResolution: false);

        if (selectedAccount is not null)
        {
            _selectedDraft.OperatorLoginName = selectedAccount.LoginName;
            _selectedDraft.OperatorErpId = selectedAccount.ErpId;
        }

        if (selectedGroup is not null)
        {
            _selectedDraft.BusinessGroupId = selectedGroup.Id;
            _selectedDraft.BusinessGroupName = selectedGroup.Name;
        }
        else
        {
            _selectedDraft.BusinessGroupId = null;
            _selectedDraft.BusinessGroupName = string.Empty;
        }

        _draftContactEditTimer.Stop();
        RefreshDraftResolution(_selectedDraft);
        GridDraftOrders.Items.Refresh();
        UpdateSelectedDraftSummary(_selectedDraft);
    }

    private void LoadDraftToForm(OrderDraft? draft)
    {
        _isLoadingDraftForm = true;
        if (draft is null)
        {
            TxtDraftReceiverName.Text = string.Empty;
            TxtDraftReceiverMobile.Text = string.Empty;
            TxtDraftReceiverAddress.Text = string.Empty;
            TxtDraftRemark.Text = string.Empty;
            ChkDraftHasGift.IsChecked = false;
            GridDraftItems.ItemsSource = null;
            TxtCurrentRawOrder.Text = "尚未选择订单。";
            UpdateSelectedDraftSummary(null);
            UpdateProductWorkflowPanel(null);
            UpdateActionAvailability();
            _isLoadingDraftForm = false;
            return;
        }

        TxtDraftReceiverName.Text = draft.ReceiverName;
        TxtDraftReceiverMobile.Text = draft.ReceiverMobile;
        TxtDraftReceiverAddress.Text = draft.ReceiverAddress;
        TxtDraftRemark.Text = draft.Remark;
        ChkDraftHasGift.IsChecked = draft.HasGift;
        if (draft.BusinessGroupId.HasValue)
        {
            CmbBusinessGroups.SelectedItem = _businessGroups.FirstOrDefault(item => item.Id == draft.BusinessGroupId.Value);
        }

        RenumberDraftItems(draft);
        GridDraftItems.ItemsSource = draft.Items;
        TxtCurrentRawOrder.Text = draft.RawText;
        TxtValidationOutput.Text = string.IsNullOrWhiteSpace(draft.StatusDetail)
            ? "待校验。"
            : draft.StatusDetail;
        UpdateSelectedDraftSummary(draft);
        UpdateProductWorkflowPanel(draft);
        UpdateActionAvailability();
        _isLoadingDraftForm = false;
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
                SearchText = item.SearchText,
                IsOutOfStock = item.IsOutOfStock
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

    private void LoadHistory(bool preserveSelection = false)
    {
        var loadVersion = ++_historyLoadVersion;
        if (!DisableHistorySyncTemporarily && _mainApiConfiguration.IsEnabled)
        {
            _allHistoryEntries = new List<OrderAuditRecord>();
            _historyTotalCount = 0;
            ApplyHistoryFilters(preserveSelection);
            _ = RefreshHistoryFromServerAsync(loadVersion, preserveSelection);
            return;
        }

        _allHistoryEntries = _auditRepository.LoadOrCreate()
            .Where(IsHistoryEntryVisibleToCurrentUser)
            .OrderByDescending(item => item.Timestamp)
            .ToList();
        ApplyHistoryEntryCapabilities(_allHistoryEntries);
        ApplyHistoryFilters(preserveSelection);
    }

    private async Task RefreshHistoryFromServerAsync(
        int loadVersion,
        bool preserveSelection,
        bool rethrowOnError = false,
        bool syncMissingLocalHistory = false)
    {
        if (DisableHistorySyncTemporarily)
        {
            return;
        }

        if (!_mainApiConfiguration.IsEnabled)
        {
            return;
        }

        try
        {
            var syncSummary = syncMissingLocalHistory
                ? await SyncMissingLocalHistoryToServerAsync()
                : HistoryServerSyncSummary.Empty;
            var serverPage = await LoadServerHistoryPageAsync();
            if (loadVersion != _historyLoadVersion)
            {
                return;
            }

            _allHistoryEntries = serverPage.Entries
                .Where(IsHistoryEntryVisibleToCurrentUser)
                .Where(item => IsFinalServerHistoryStatus(item.Status))
                .OrderByDescending(item => item.Timestamp)
                .ToList();
            ApplyHistoryEntryCapabilities(_allHistoryEntries);
            ApplyHistoryFilters(preserveSelection, serverPage.TotalCount);
            TxtStatus.Text = syncSummary.UploadedCount > 0 || syncSummary.FailedCount > 0
                ? $"历史记录已以服务器为准，补传 {syncSummary.UploadedCount} 条，失败 {syncSummary.FailedCount} 条，共 {_historyTotalCount} 条。"
                : $"历史记录已以服务器为准，共 {_historyTotalCount} 条。";
        }
        catch (Exception ex)
        {
            if (loadVersion != _historyLoadVersion)
            {
                return;
            }

            TxtStatus.Text = $"历史记录已显示本地数据，服务器同步失败：{ex.Message}";
            if (rethrowOnError)
            {
                throw;
            }
        }
    }

    private async Task RefreshHistoryAfterUploadAsync()
    {
        try
        {
            await LoadHistoryBusinessGroupsAsync();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"历史业务群下拉刷新失败：{ex.Message}";
        }

        var loadVersion = ++_historyLoadVersion;
        await RefreshHistoryFromServerAsync(loadVersion, preserveSelection: IsHistoryTabActive());
    }

    private async Task RefreshAllServerDataAsync()
    {
        if (_isRefreshingServerData)
        {
            return;
        }

        if (_session is null || !_session.Configuration.IsEnabled)
        {
            TxtStatus.Text = "当前未启用服务器联动，无法刷新服务器数据。";
            return;
        }

        var preserveHistorySelection = IsHistoryTabActive();
        var syncWindow = new SyncProgressWindow("正在刷新业务群，请稍候…") { Owner = this };
        _isRefreshingServerData = true;
        BtnRefreshServerData.IsEnabled = false;
        IsEnabled = false;
        syncWindow.Show();

        try
        {
            syncWindow.UpdateMessage("正在刷新业务群，请稍候…");
            await LoadBusinessGroupsAsync(preserveSelection: true, rethrowOnError: true);

            syncWindow.UpdateMessage("正在刷新周期设置和商品编码，请稍候…");
            await SyncCatalogFromServerAsync(showStatus: false, rethrowOnError: true);

            syncWindow.UpdateMessage("正在刷新订单数据，请稍候…");
            var loadVersion = ++_historyLoadVersion;
            await RefreshHistoryFromServerAsync(
                loadVersion,
                preserveHistorySelection,
                rethrowOnError: true,
                syncMissingLocalHistory: true);

            _lastServerSyncCheckUtc = DateTime.UtcNow;
            UpdateLastRefreshTimeText(DateTime.Now);
            TxtStatus.Text = $"服务器数据刷新完成：业务群 {_businessGroups.Count} 个，商品 {_productCatalog.Count} 条，订单数据 {_historyEntries.Count} 条。";
            TxtSettingsStatus.Text = "业务群、周期设置、商品编码和订单数据已刷新为服务器最新结果。";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"刷新服务器数据失败：{ex.Message}";
        }
        finally
        {
            syncWindow.Close();
            IsEnabled = true;
            BtnRefreshServerData.IsEnabled = true;
            _isRefreshingServerData = false;
            Activate();
        }
    }

    private void UpdateLastRefreshTimeText(DateTime? refreshedAtLocal = null)
    {
        if (TxtLastRefreshTime is null)
        {
            return;
        }

        TxtLastRefreshTime.Text = refreshedAtLocal.HasValue
            ? $"最近刷新：{refreshedAtLocal.Value:yyyy-MM-dd HH:mm:ss}"
            : "最近刷新：未执行";
    }

    private async Task<HistoryPageResult> LoadServerHistoryPageAsync()
    {
        var orderNumber = TxtHistoryOrderNumberFilter.Text.Trim();
        var receiverKeyword = TxtHistoryReceiverFilter.Text.Trim();
        var businessGroupName = GetSelectedHistoryBusinessGroupName();
        var operatorLoginName = IsHistoryAdministrator()
            ? TxtHistoryOperatorFilter.Text.Trim()
            : GetHistoryUploaderLoginName();
        var dateFrom = DpHistoryStartDate.SelectedDate?.Date;
        var dateTo = DpHistoryEndDate.SelectedDate?.Date;

        var currentLoginName = GetCurrentHistoryOperatorLoginName();
        if (string.IsNullOrWhiteSpace(currentLoginName) ||
            (!IsHistoryAdministrator() &&
             (!string.Equals(operatorLoginName, currentLoginName, StringComparison.OrdinalIgnoreCase))))
        {
            return HistoryPageResult.Empty;
        }

        var page = await _mainApiSyncClient.QueryUploadsAsync(
            _mainApiConfiguration,
            pageNumber: _historyPageNumber,
            pageSize: _historyPageSize,
            uploaderLoginName: operatorLoginName,
            orderNumber: orderNumber,
            receiverKeyword: receiverKeyword,
            businessGroupName: businessGroupName,
            dateFrom: dateFrom,
            dateTo: dateTo);

        return new HistoryPageResult(
            page.TotalCount,
            page.PageNumber,
            page.Items.Select(MapUploadToHistoryRecord).ToList());
    }

    private async Task<List<OrderAuditRecord>> QueryServerHistoryEntriesAsync(
        string orderNumber = "",
        string receiverKeyword = "",
        string operatorLoginName = "",
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        Action<List<OrderAuditRecord>>? firstPageLoaded = null,
        bool includeAllUploadersForAdministrator = false)
    {
        var currentLoginName = GetCurrentHistoryOperatorLoginName();
        if (string.IsNullOrWhiteSpace(currentLoginName) ||
            (!includeAllUploadersForAdministrator &&
             !string.IsNullOrWhiteSpace(operatorLoginName) &&
             !string.Equals(operatorLoginName.Trim(), currentLoginName, StringComparison.OrdinalIgnoreCase)))
        {
            return new List<OrderAuditRecord>();
        }

        var uploaderLoginName = includeAllUploadersForAdministrator && IsHistoryAdministrator()
            ? string.Empty
            : currentLoginName;

        var entries = new List<OrderAuditRecord>();
        const int pageSize = 200;
        var pageNumber = 1;

        while (true)
        {
            var page = await _mainApiSyncClient.QueryUploadsAsync(
                _mainApiConfiguration,
                pageNumber: pageNumber,
                pageSize: pageSize,
                uploaderLoginName: uploaderLoginName,
                orderNumber: orderNumber,
                receiverKeyword: receiverKeyword,
                dateFrom: dateFrom,
                dateTo: dateTo,
                includeContent: true);

            if (page.Items.Count == 0)
            {
                break;
            }

            entries.AddRange(page.Items.Select(MapUploadToHistoryRecord));

            if (pageNumber == 1 && firstPageLoaded is not null)
            {
                var firstPageEntries = entries
                    .Where(IsHistoryEntryVisibleToCurrentUser)
                    .Where(item => IsFinalServerHistoryStatus(item.Status))
                    .ToList();
                ApplyLatestHistoryStatus(firstPageEntries);
                firstPageLoaded(firstPageEntries);
            }

            if (entries.Count >= page.TotalCount || page.Items.Count < pageSize)
            {
                break;
            }

            pageNumber++;
        }

        entries = entries
            .Where(IsHistoryEntryVisibleToCurrentUser)
            .Where(item => IsFinalServerHistoryStatus(item.Status))
            .ToList();
        ApplyLatestHistoryStatus(entries);
        return entries;
    }

    private async Task<ServerOrderState> GetServerOrderStateAsync(string? orderNumber)
    {
        var normalizedOrderNumber = orderNumber?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedOrderNumber) || !_mainApiConfiguration.IsEnabled)
        {
            return ServerOrderState.Empty;
        }

        var entries = await QueryServerHistoryEntriesAsync(
            orderNumber: normalizedOrderNumber,
            includeAllUploadersForAdministrator: IsHistoryAdministrator());
        var exactEntries = entries
            .Where(item => string.Equals(item.OrderNumber?.Trim(), normalizedOrderNumber, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var hasSuccessfulUpload = exactEntries.Any(item => IsSuccessfulUploadStatus(item.Status));
        var hasSuccessfulCancellation = exactEntries.Any(item =>
            string.Equals(NormalizeHistoryStatus(item.Status), "已取消", StringComparison.OrdinalIgnoreCase));

        return new ServerOrderState(hasSuccessfulUpload, hasSuccessfulCancellation, exactEntries.Count);
    }

    private async Task<HistoryServerSyncSummary> SyncMissingLocalHistoryToServerAsync()
    {
        var localEntries = _auditRepository.LoadOrCreate()
            .Where(IsHistoryEntryOwnedByCurrentUser)
            .Where(ShouldSyncHistoryToServer)
            .OrderBy(item => item.Timestamp)
            .ToList();
        if (localEntries.Count == 0)
        {
            return HistoryServerSyncSummary.Empty;
        }

        var serverEntries = await QueryServerHistoryEntriesAsync();
        var serverFingerprints = serverEntries
            .Select(BuildHistorySyncFingerprint)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var uploadedCount = 0;
        var failedCount = 0;

        foreach (var entry in localEntries)
        {
            var fingerprint = BuildHistorySyncFingerprint(entry);
            if (string.IsNullOrWhiteSpace(fingerprint) || serverFingerprints.Contains(fingerprint))
            {
                continue;
            }

            try
            {
                var draft = BuildDraftFromHistoryEntry(entry);
                draft.Status = string.IsNullOrWhiteSpace(entry.Status) ? draft.Status : entry.Status.Trim();
                draft.StatusDetail = string.IsNullOrWhiteSpace(entry.ResponseText)
                    ? draft.StatusDetail
                    : entry.ResponseText.Trim();

                ApplyFallbackContextToDraft(draft);
                await _mainApiSyncClient.SyncUploadAsync(
                    draft,
                    _mainApiConfiguration,
                    draft.RawText,
                    entry.SnapshotJson,
                    "{}",
                    entry.ResponseText ?? string.Empty,
                    entry.Timestamp);

                serverFingerprints.Add(fingerprint);
                uploadedCount++;
            }
            catch
            {
                failedCount++;
            }
        }

        return new HistoryServerSyncSummary(uploadedCount, failedCount);
    }

    private static OrderAuditRecord MapUploadToHistoryRecord(MainApiSyncClient.UploadSummaryItem item)
    {
        return new OrderAuditRecord
        {
            RecordId = $"upload-{item.Id}",
            ServerUploadId = item.Id,
            DraftId = item.DraftId ?? string.Empty,
            OrderNumber = item.OrderNumber ?? string.Empty,
            SessionId = item.SessionId ?? string.Empty,
            Timestamp = item.CreatedAtUtc.Kind == DateTimeKind.Utc ? item.CreatedAtUtc.ToLocalTime() : item.CreatedAtUtc,
            ActionType = ResolveUploadActionType(item.Status),
            ReceiverName = item.ReceiverName ?? string.Empty,
            ReceiverMobile = item.ReceiverMobile ?? string.Empty,
            ReceiverAddress = item.ReceiverAddress ?? string.Empty,
            GoodsSummary = item.ItemCount <= 0 ? string.Empty : $"共 {item.ItemCount} 项商品",
            Status = item.Status ?? string.Empty,
            OperatorLoginName = item.UploaderLoginName ?? string.Empty,
            BusinessGroupId = item.BusinessGroupId,
            BusinessGroupName = item.BusinessGroupName ?? string.Empty,
            RawText = item.RawText ?? string.Empty,
            SnapshotJson = item.SnapshotJson ?? string.Empty,
            ResponseText = item.ResponseText ?? string.Empty
        };
    }

    private static void ApplyServerHistoryDetail(OrderAuditRecord entry, MainApiSyncClient.UploadDetailResult detail)
    {
        entry.RawText = detail.RawText ?? string.Empty;
        entry.SnapshotJson = detail.SnapshotJson ?? string.Empty;
        entry.ResponseText = detail.ResponseText ?? string.Empty;
        entry.IsServerDetailLoaded = true;
    }

    private static string ResolveUploadActionType(string? status)
    {
        var value = status?.Trim() ?? string.Empty;
        if (value.Contains("跳过", StringComparison.OrdinalIgnoreCase))
        {
            return "跳过订单";
        }

        if (value.Contains("取消", StringComparison.OrdinalIgnoreCase))
        {
            return "取消订单";
        }

        if (value.Contains("失败", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("异常", StringComparison.OrdinalIgnoreCase))
        {
            return "上传失败";
        }

        return "上传订单";
    }

    private static void ApplyLatestHistoryStatus(List<OrderAuditRecord> entries)
    {
        var cancelledKeys = entries
            .Where(item => string.Equals(item.Status, "已取消", StringComparison.OrdinalIgnoreCase))
            .Select(item => BuildHistoryIdentityKey(item.DraftId, item.OrderNumber))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (cancelledKeys.Count == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            var key = BuildHistoryIdentityKey(entry.DraftId, entry.OrderNumber);
            if (string.IsNullOrWhiteSpace(key) ||
                !cancelledKeys.Contains(key) ||
                string.Equals(entry.Status, "已取消", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entry.Status = "已取消";
        }
    }

    private static string BuildHistoryIdentityKey(string? draftId, string? orderNumber)
    {
        if (!string.IsNullOrWhiteSpace(draftId))
        {
            return $"draft:{draftId.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(orderNumber))
        {
            return $"order:{orderNumber.Trim()}";
        }

        return string.Empty;
    }

    private static string NormalizeHistoryStatus(string? status)
    {
        return status?.Trim() ?? string.Empty;
    }

    private static bool IsCancellationHistoryStatus(string? status)
    {
        var value = NormalizeHistoryStatus(status);
        return value.Contains("取消", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "已取消", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFailureHistoryStatus(string? status)
    {
        var value = NormalizeHistoryStatus(status);
        return value.Contains("失败", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("异常", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSkippedHistoryStatus(string? status)
    {
        return NormalizeHistoryStatus(status).Contains("跳过", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuccessfulUploadStatus(string? status)
    {
        var value = NormalizeHistoryStatus(status);
        return !string.IsNullOrWhiteSpace(value) &&
               !IsCancellationHistoryStatus(value) &&
               !IsFailureHistoryStatus(value) &&
               !IsSkippedHistoryStatus(value);
    }

    private static bool ShouldSyncHistoryToServer(OrderAuditRecord entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.SnapshotJson))
        {
            return false;
        }

        return ShouldPersistHistory(entry.ActionType) && IsFinalServerHistoryStatus(entry.Status);
    }

    private static bool IsFinalServerHistoryStatus(string? status)
    {
        var value = NormalizeHistoryStatus(status);
        return string.Equals(value, "上传成功", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "已取消", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyHistoryEntryCapabilities(IEnumerable<OrderAuditRecord> entries)
    {
        foreach (var entry in entries)
        {
            entry.CanCancel = CanCurrentUserCancelHistoryEntry(entry);
        }
    }

    private static string BuildHistorySyncFingerprint(OrderAuditRecord entry)
    {
        var identityKey = BuildHistoryIdentityKey(entry.DraftId, entry.OrderNumber);
        if (string.IsNullOrWhiteSpace(identityKey))
        {
            return string.Empty;
        }

        return string.Join(
            "|",
            identityKey,
            NormalizeHistoryFingerprintPart(entry.Status),
            ComputeHistoryPayloadHash(entry.SnapshotJson),
            ComputeHistoryPayloadHash(entry.ResponseText));
    }

    private static string NormalizeHistoryFingerprintPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string ComputeHistoryPayloadHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes);
    }

    private void ApplyHistoryFilters(bool preserveSelection = true, int? serverTotalCount = null)
    {
        var selectedRecordId = preserveSelection
            ? (GridHistory.SelectedItem as OrderAuditRecord)?.RecordId
            : null;
        var orderNumber = TxtHistoryOrderNumberFilter.Text.Trim();
        var receiverName = TxtHistoryReceiverFilter.Text.Trim();
        var businessGroupName = GetSelectedHistoryBusinessGroupName();
        var operatorLoginName = TxtHistoryOperatorFilter.Text.Trim();
        var startDate = DpHistoryStartDate.SelectedDate?.Date;
        var endDateExclusive = DpHistoryEndDate.SelectedDate?.Date.AddDays(1);

        IEnumerable<OrderAuditRecord> query = _allHistoryEntries
            .Where(IsHistoryEntryVisibleToCurrentUser);

        if (!string.IsNullOrWhiteSpace(orderNumber))
        {
            query = query.Where(item =>
                string.Equals(item.OrderNumber?.Trim(), orderNumber, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(receiverName))
        {
            query = query.Where(item =>
                !string.IsNullOrWhiteSpace(item.ReceiverName) &&
                item.ReceiverName.Contains(receiverName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(operatorLoginName))
        {
            query = query.Where(item =>
                !string.IsNullOrWhiteSpace(item.OperatorLoginName) &&
                item.OperatorLoginName.Contains(operatorLoginName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(businessGroupName))
        {
            query = query.Where(item =>
                string.Equals(item.BusinessGroupName?.Trim(), businessGroupName, StringComparison.OrdinalIgnoreCase));
        }

        if (startDate.HasValue)
        {
            query = query.Where(item => item.Timestamp >= startDate.Value);
        }

        if (endDateExclusive.HasValue)
        {
            query = query.Where(item => item.Timestamp < endDateExclusive.Value);
        }

        var filteredEntries = query
            .OrderByDescending(item => item.Timestamp)
            .ToList();
        _historyTotalCount = serverTotalCount ?? filteredEntries.Count;
        if (serverTotalCount.HasValue)
        {
            _historyEntries = new ObservableCollection<OrderAuditRecord>(filteredEntries);
        }
        else
        {
            _historyEntries = new ObservableCollection<OrderAuditRecord>(filteredEntries
                .Skip((_historyPageNumber - 1) * _historyPageSize)
                .Take(_historyPageSize));
        }

        UpdateHistorySequenceNumbers();
        GridHistory.ItemsSource = _historyEntries;
        UpdateHistoryPaginationControls();

        if (!string.IsNullOrWhiteSpace(selectedRecordId))
        {
            var selected = _historyEntries.FirstOrDefault(item =>
                string.Equals(item.RecordId, selectedRecordId, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                GridHistory.SelectedItem = selected;
                GridHistory.ScrollIntoView(selected);
                TxtStatus.Text = $"历史记录筛选完成，共 {_historyTotalCount} 条。";
                return;
            }
        }

        GridHistory.SelectedItem = null;
        TxtHistoryRaw.Text = _historyEntries.Count == 0 ? "没有符合条件的历史记录。" : "请选择一条历史记录。";
        TxtHistorySnapshot.Text = _historyEntries.Count == 0 ? "没有符合条件的历史记录。" : "请选择一条历史记录。";
        TxtHistoryResponse.Text = _historyEntries.Count == 0 ? "没有符合条件的历史记录。" : "请选择一条历史记录。";
        TxtStatus.Text = $"历史记录筛选完成，共 {_historyTotalCount} 条。";
    }

    private void UpdateHistorySequenceNumbers()
    {
        int firstSequenceNumber = (_historyPageNumber - 1) * _historyPageSize + 1;
        for (int index = 0; index < _historyEntries.Count; index++)
        {
            _historyEntries[index].SequenceNumber = firstSequenceNumber + index;
        }
    }

    private int GetHistoryPageCount()
    {
        return _historyTotalCount == 0
            ? 0
            : (int)Math.Ceiling((double)_historyTotalCount / _historyPageSize);
    }

    private void UpdateHistoryPaginationControls()
    {
        var pageCount = GetHistoryPageCount();
        if (pageCount > 0 && _historyPageNumber > pageCount)
        {
            _historyPageNumber = pageCount;
        }

        TxtHistoryPageSummary.Text = pageCount == 0
            ? "共 0 条，第 0 / 0 页"
            : $"共 {_historyTotalCount} 条，第 {_historyPageNumber} / {pageCount} 页";
        BtnHistoryPreviousPage.IsEnabled = _historyPageNumber > 1;
        BtnHistoryNextPage.IsEnabled = pageCount > 0 && _historyPageNumber < pageCount;
    }

    private void RefreshLookupSources()
    {
        ColSettingsWearMapping.ItemsSource = _wearPeriods.ToList();

        var previouslySelectedLogin = (CmbOperatorAccounts.SelectedItem as UserAccountRow)?.LoginName;
        CmbOperatorAccounts.ItemsSource = _userAccounts;

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
                    SearchText = Safe(item.SearchText),
                    IsOutOfStock = item.IsOutOfStock
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

    private void UpdateValidationResultText(string text)
    {
        TxtValidationOutput.Text = text;
        TxtStatus.Text = text;
    }

    private void ValidateDraft(OrderDraft draft)
    {
        var snapshot = BuildSnapshotFromUi();
        _catalogSkuResolver.RefreshDraft(draft, snapshot);
        var validation = _draftValidator.Validate(draft, snapshot);
        draft.Status = validation.IsValid ? "待上传" : "待补全";
        draft.StatusDetail = validation.ToString();
        UpdateValidationResultText(validation.ToString());
        SaveHistoryEntry(draft, draft.StatusDetail, "校验订单");
    }

    private async Task<bool> UploadDraftAsync(OrderDraft draft, bool moveToNext)
    {
        var snapshot = BuildSnapshotFromUi();
        if (!draft.BusinessGroupId.HasValue || string.IsNullOrWhiteSpace(draft.BusinessGroupName))
        {
            draft.Status = "待补全";
            draft.StatusDetail = "请选择右上角业务群后再上传。";
            TxtUploadOutput.Text = draft.StatusDetail;
            ShowUploadBlockingDialog("上传失败", draft.StatusDetail);
            RefreshDraftViews();
            GridDraftOrders.SelectedItem = draft;
            GridDraftOrders.ScrollIntoView(draft);
            return false;
        }

        _catalogSkuResolver.RefreshDraft(draft, snapshot);
        var validation = _draftValidator.Validate(draft, snapshot);
        var uploadBlockers = BuildUploadBlockingMessage(draft, validation);
        if (!string.IsNullOrWhiteSpace(uploadBlockers))
        {
            draft.Status = "待补全";
            draft.StatusDetail = uploadBlockers;
            UpdateValidationResultText(draft.StatusDetail);
            TxtUploadOutput.Text = draft.StatusDetail;
            TxtStatus.Text = "上传已拦截，请先处理提示项。";
            ShowUploadBlockingDialog("上传失败", draft.StatusDetail);
            RefreshDraftViews();
            GridDraftOrders.SelectedItem = draft;
            GridDraftOrders.ScrollIntoView(draft);
            return false;
        }

        if (snapshot.MainApi.IsEnabled)
        {
            try
            {
                var serverState = await GetServerOrderStateAsync(draft.OrderNumber);
                if (serverState.HasSuccessfulUpload)
                {
                    draft.Status = "上传失败";
                    draft.StatusDetail = serverState.HasSuccessfulCancellation
                        ? "云端已存在该订单号的上传和取消记录，不能重复上传。"
                        : "云端已存在该订单号的上传记录，不能重复上传。";
                    TxtUploadOutput.Text = draft.StatusDetail;
                    TxtStatus.Text = "上传已拦截，请勿重复上传。";
                    ShowUploadBlockingDialog("上传失败", draft.StatusDetail);
                    RefreshDraftViews();
                    GridDraftOrders.SelectedItem = draft;
                    GridDraftOrders.ScrollIntoView(draft);
                    return false;
                }
            }
            catch (Exception ex)
            {
                draft.Status = "上传失败";
                draft.StatusDetail = $"云端查重失败，已阻止上传：{ex.Message}";
                TxtUploadOutput.Text = draft.StatusDetail;
                TxtStatus.Text = "上传失败。";
                ShowUploadBlockingDialog("上传失败", draft.StatusDetail);
                RefreshDraftViews();
                GridDraftOrders.SelectedItem = draft;
                GridDraftOrders.ScrollIntoView(draft);
                return false;
            }
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

            if (result.IsSuccess)
            {
                try
                {
                    await _mainApiSyncClient.SyncUploadAsync(
                        draft,
                        snapshot.MainApi,
                        draft.RawText,
                        BuildDraftSnapshotJson(draft),
                        JsonSerializer.Serialize(result.RequestFields, JsonOptions),
                        result.ResponseText);
                }
                catch (Exception syncEx)
                {
                    draft.StatusDetail = $"{draft.StatusDetail}{Environment.NewLine}{Environment.NewLine}MainApi 记录失败：{syncEx.Message}";
                }
            }

            UpdateValidationResultText(validation.ToString());
            TxtUploadOutput.Text = draft.StatusDetail;
            if (result.IsSuccess)
            {
                SaveHistoryEntry(draft, draft.StatusDetail, "上传成功");
                SaveUploadLearningSample(draft, result);
                TxtStatus.Text = $"订单 {DisplayValue(draft.OrderNumber, draft.DraftId)} 上传成功。";
                ShowToastMessage($"订单 {DisplayValue(draft.OrderNumber, draft.DraftId)} 上传成功。");
            }
            else
            {
                TxtStatus.Text = "上传失败。";
                ShowUploadBlockingDialog("上传失败", draft.StatusDetail);
            }

            return result.IsSuccess;
        }
        catch (Exception ex)
        {
            draft.Status = "上传失败";
            draft.StatusDetail = ex.ToString();
            TxtUploadOutput.Text = ex.ToString();
            TxtStatus.Text = "上传失败。";
            ShowUploadBlockingDialog("上传失败", draft.StatusDetail);
            return false;
        }
        finally
        {
            RefreshDraftViews();
            if (moveToNext && string.Equals(draft.Status, "上传成功", StringComparison.OrdinalIgnoreCase))
            {
                MoveToNextDraft();
            }
            else if (!string.Equals(draft.Status, "上传成功", StringComparison.OrdinalIgnoreCase))
            {
                GridDraftOrders.SelectedItem = draft;
                GridDraftOrders.ScrollIntoView(draft);
            }

            UpdateActionAvailability();
        }
    }

    private async Task CancelHistoryOrderAsync(OrderAuditRecord entry)
    {
        if (string.Equals(entry.Status, "已取消", StringComparison.OrdinalIgnoreCase))
        {
            var message = $"订单 {DisplayValue(entry.OrderNumber, entry.DraftId)} 已取消。";
            TxtStatus.Text = message;
            ShowToastMessage(message);
            SelectHistoryEntry(entry.RecordId);
            return;
        }

        try
        {
            await EnsureHistoryEntryDetailsLoadedAsync(entry);
        }
        catch (Exception ex)
        {
            var message = $"历史记录详情加载失败，无法取消订单：{ex.Message}";
            TxtStatus.Text = message;
            TxtHistoryResponse.Text = message;
            ShowUploadBlockingDialog("取消订单失败", message);
            return;
        }

        OrderDraft draft;
        try
        {
            draft = BuildDraftFromHistoryEntry(entry);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "历史记录无法还原为订单草稿。";
            TxtHistoryResponse.Text = ex.Message;
            MessageBox.Show(ex.Message, "取消订单失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ApplyFallbackContextToDraft(draft);
        if (_mainApiConfiguration.IsEnabled)
        {
            try
            {
                var serverState = await GetServerOrderStateAsync(draft.OrderNumber);
                if (!serverState.HasSuccessfulUpload)
                {
                    const string message = "云端没有该订单号的已上传记录，无法取消。";
                    TxtStatus.Text = message;
                    TxtHistoryResponse.Text = message;
                    ShowUploadBlockingDialog("取消订单失败", message);
                    return;
                }

                if (serverState.HasSuccessfulCancellation)
                {
                    const string message = "该订单号云端已取消，无需重复取消。";
                    TxtStatus.Text = message;
                    TxtHistoryResponse.Text = message;
                    ShowUploadBlockingDialog("取消订单失败", message);
                    return;
                }
            }
            catch (Exception ex)
            {
                var message = $"云端取消校验失败：{ex.Message}";
                TxtStatus.Text = message;
                TxtHistoryResponse.Text = message;
                ShowUploadBlockingDialog("取消订单失败", message);
                return;
            }
        }

        var snapshot = BuildSnapshotFromUi();
        var originalCanCancel = entry.CanCancel;
        entry.CanCancel = false;
        TxtStatus.Text = $"正在取消订单 {DisplayValue(draft.OrderNumber, draft.DraftId)} ...";

        try
        {
            var result = await _tradeUploader.UploadAsync(
                draft,
                snapshot.Upload,
                HupunB2cTradeUploader.CancelUploadTradeStatus);

            draft.Status = result.IsSuccess ? "已取消" : "取消失败";
            draft.StatusDetail = string.IsNullOrWhiteSpace(result.FriendlyMessage)
                ? result.DebugText
                : $"{result.FriendlyMessage}{Environment.NewLine}{Environment.NewLine}{result.DebugText}";

            if (result.IsSuccess)
            {
                try
                {
                    await _mainApiSyncClient.SyncUploadAsync(
                        draft,
                        snapshot.MainApi,
                        draft.RawText,
                        BuildDraftSnapshotJson(draft),
                        JsonSerializer.Serialize(result.RequestFields, JsonOptions),
                        result.ResponseText);
                }
                catch (Exception syncEx)
                {
                    draft.StatusDetail = $"{draft.StatusDetail}{Environment.NewLine}{Environment.NewLine}MainApi 记录失败：{syncEx.Message}";
                }
            }

            if (result.IsSuccess)
            {
                ApplyCancellationToMatchingHistoryEntries(entry, draft.Status, draft.StatusDetail);
            }

            TxtUploadOutput.Text = draft.StatusDetail;
            TxtHistoryResponse.Text = draft.StatusDetail;
            SaveHistoryEntry(draft, draft.StatusDetail, result.IsSuccess ? "取消订单" : "取消失败");
            TxtStatus.Text = result.IsSuccess
                ? $"订单 {DisplayValue(draft.OrderNumber, draft.DraftId)} 已取消。"
                : $"订单 {DisplayValue(draft.OrderNumber, draft.DraftId)} 取消失败。";
            if (result.IsSuccess)
            {
                ShowToastMessage($"订单 {DisplayValue(draft.OrderNumber, draft.DraftId)} 已取消。");
            }
            else
            {
                ShowUploadBlockingDialog("取消订单失败", draft.StatusDetail);
            }
        }
        catch (Exception ex)
        {
            draft.Status = "取消失败";
            draft.StatusDetail = ex.ToString();
            TxtUploadOutput.Text = ex.ToString();
            TxtHistoryResponse.Text = ex.ToString();
            entry.CanCancel = originalCanCancel;
            SaveHistoryEntry(draft, ex.ToString(), "取消异常");
            TxtStatus.Text = $"订单 {DisplayValue(draft.OrderNumber, draft.DraftId)} 取消异常。";
            ShowUploadBlockingDialog("取消订单失败", draft.StatusDetail);
        }
        finally
        {
            RefreshDraftViews();
            UpdateActionAvailability();
            SelectHistoryEntry(entry.RecordId);
        }
    }

    private void ApplyFallbackContextToDraft(OrderDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.OperatorLoginName))
        {
            draft.OperatorLoginName =
                _session?.User.LoginName?.Trim() ??
                (CmbOperatorAccounts.SelectedItem as UserAccountRow)?.LoginName?.Trim() ??
                string.Empty;
        }

        if (string.IsNullOrWhiteSpace(draft.OperatorErpId))
        {
            draft.OperatorErpId =
                _session?.User.ErpId?.Trim() ??
                (CmbOperatorAccounts.SelectedItem as UserAccountRow)?.ErpId?.Trim() ??
                string.Empty;
        }

        if ((!draft.BusinessGroupId.HasValue || string.IsNullOrWhiteSpace(draft.BusinessGroupName)) &&
            CmbBusinessGroups.SelectedItem is BusinessGroupOption selectedGroup)
        {
            draft.BusinessGroupId ??= selectedGroup.Id;
            if (string.IsNullOrWhiteSpace(draft.BusinessGroupName))
            {
                draft.BusinessGroupName = selectedGroup.Name;
            }
        }
    }

    private static OrderDraft BuildDraftFromHistoryEntry(OrderAuditRecord entry)
    {
        if (string.IsNullOrWhiteSpace(entry.SnapshotJson))
        {
            throw new InvalidOperationException("历史记录里没有订单快照，暂时无法执行取消订单。");
        }

        HistoryDraftSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<HistoryDraftSnapshot>(entry.SnapshotJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("历史记录快照解析失败，无法执行取消订单。", ex);
        }

        if (snapshot is null)
        {
            throw new InvalidOperationException("历史记录快照为空，无法执行取消订单。");
        }

        var draft = new OrderDraft
        {
            DraftId = CoalesceHistoryValue(snapshot.DraftId, entry.DraftId),
            OrderNumber = CoalesceHistoryValue(snapshot.OrderNumber, entry.OrderNumber),
            SessionId = CoalesceHistoryValue(snapshot.SessionId, entry.SessionId),
            OrderIndex = snapshot.OrderIndex,
            RawText = CoalesceHistoryValue(entry.RawText, snapshot.RawText),
            ReceiverName = CoalesceHistoryValue(snapshot.ReceiverName, entry.ReceiverName),
            ReceiverMobile = CoalesceHistoryValue(snapshot.ReceiverMobile, entry.ReceiverMobile),
            ReceiverProvince = snapshot.ReceiverProvince ?? string.Empty,
            ReceiverCity = snapshot.ReceiverCity ?? string.Empty,
            ReceiverArea = snapshot.ReceiverArea ?? string.Empty,
            ReceiverRegion = snapshot.ReceiverRegion ?? string.Empty,
            ReceiverAddress = CoalesceHistoryValue(snapshot.ReceiverAddress, entry.ReceiverAddress),
            Remark = snapshot.Remark ?? string.Empty,
            HasGift = snapshot.HasGift,
            OperatorLoginName = snapshot.OperatorLoginName ?? string.Empty,
            OperatorErpId = snapshot.OperatorErpId ?? string.Empty,
            BusinessGroupId = snapshot.BusinessGroupId,
            BusinessGroupName = snapshot.BusinessGroupName ?? string.Empty,
            Status = CoalesceHistoryValue(snapshot.Status, entry.Status),
            StatusDetail = snapshot.StatusDetail ?? string.Empty
        };

        foreach (var item in snapshot.Items)
        {
            draft.Items.Add(new OrderItemDraft
            {
                SourceText = item.SourceText ?? string.Empty,
                ProductCode = item.ProductCode ?? string.Empty,
                ProductName = item.ProductName ?? string.Empty,
                SpecCodeText = item.SpecCodeText ?? string.Empty,
                BarcodeText = item.BarcodeText ?? string.Empty,
                WearPeriod = item.WearPeriod ?? string.Empty,
                QuantityText = string.IsNullOrWhiteSpace(item.QuantityText) ? "1" : item.QuantityText,
                Remark = item.Remark ?? string.Empty,
                DegreeText = item.DegreeText ?? string.Empty,
                IsTrial = item.IsTrial,
                IsOutOfStock = item.IsOutOfStock,
                MatchHint = item.MatchHint ?? string.Empty,
                ProductCodeConfirmed = !string.IsNullOrWhiteSpace(item.ProductCode),
                UseManualProductCodeStyle = item.UseManualProductCodeStyle
            });
        }

        if (draft.Items.Count == 0)
        {
            throw new InvalidOperationException("历史记录快照里没有商品明细，无法执行取消订单。");
        }

        return draft;
    }

    private void ApplyCancellationToMatchingHistoryEntries(OrderAuditRecord entry, string status, string responseText)
    {
        var identityKey = BuildHistoryIdentityKey(entry.DraftId, entry.OrderNumber);
        if (string.IsNullOrWhiteSpace(identityKey))
        {
            UpdateHistoryEntryState(entry, status, responseText);
            _ = PersistUpdatedHistoryEntriesAsync(new[] { entry });
            return;
        }

        var affectedEntries = _allHistoryEntries
            .Where(item => string.Equals(BuildHistoryIdentityKey(item.DraftId, item.OrderNumber), identityKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (affectedEntries.Count == 0)
        {
            affectedEntries.Add(entry);
        }

        foreach (var affectedEntry in affectedEntries)
        {
            UpdateHistoryEntryState(affectedEntry, status, responseText);
        }

        _ = PersistUpdatedHistoryEntriesAsync(affectedEntries);
    }

    private void UpdateHistoryEntryState(OrderAuditRecord entry, string status, string responseText)
    {
        entry.Status = status;
        entry.ResponseText = responseText;
        entry.CanCancel = CanCurrentUserCancelHistoryEntry(entry);
    }

    private void ShowToastMessage(string message)
    {
        TxtToast.Text = message;
        ToastHost.Background = CreateFrozenBrush("#DCFCE7");
        ToastHost.BorderBrush = CreateFrozenBrush("#86EFAC");
        TxtToast.Foreground = CreateFrozenBrush("#166534");
        ToastHost.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ShowUploadBlockingDialog(string title, string message)
    {
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string BuildUploadBlockingMessage(OrderDraft draft, OrderValidationResult validation)
    {
        var reasons = new List<string>();
        if (!validation.IsValid)
        {
            reasons.Add(validation.ToString());
        }

        // var outOfStockItems = draft.Items
        //     .Where(item => item.IsOutOfStock)
        //     .Select(FormatBlockingItem)
        //     .ToArray();
        // if (outOfStockItems.Length > 0)
        // {
        //     reasons.Add($"存在缺货商品：{string.Join("、", outOfStockItems)}。");
        // }

        var unmatchedItems = draft.Items
            .Where(item => string.Equals(item.ProductMatchState, "Unmatched", StringComparison.OrdinalIgnoreCase))
            .Select(FormatBlockingItem)
            .ToArray();
        if (unmatchedItems.Length > 0)
        {
            reasons.Add($"存在未匹配商品：{string.Join("、", unmatchedItems)}。");
        }

        var partialItems = draft.Items
            .Where(item =>
                string.Equals(item.ProductMatchState, "Partial", StringComparison.OrdinalIgnoreCase) ||
                (!item.ProductCodeConfirmed && !string.Equals(item.ProductMatchState, "Exact", StringComparison.OrdinalIgnoreCase)))
            .Select(FormatBlockingItem)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (partialItems.Length > 0)
        {
            reasons.Add($"存在待确认商品：{string.Join("、", partialItems)}。");
        }

        return reasons.Count == 0
            ? string.Empty
            : $"上传失败，已阻止上传。{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, reasons)}";
    }

    private static string FormatBlockingItem(OrderItemDraft item)
    {
        var prefix = item.SequenceNumber > 0 ? $"{item.SequenceNumber}." : string.Empty;
        var name = Safe(item.ProductName);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = item.ProductCodeOrPlaceholder;
        }

        var degree = Safe(item.DegreeText);
        var quantity = Safe(item.QuantityText);
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            details.Add(name);
        }

        if (!string.IsNullOrWhiteSpace(degree))
        {
            details.Add($"{degree}度");
        }

        if (!string.IsNullOrWhiteSpace(quantity))
        {
            details.Add($"x{quantity}");
        }

        return $"{prefix}{string.Join(" ", details)}";
    }

    private void SelectHistoryEntry(string? recordId)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return;
        }

        var target = _historyEntries.FirstOrDefault(item =>
            string.Equals(item.RecordId, recordId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        GridHistory.SelectedItem = target;
        GridHistory.ScrollIntoView(target);
    }

    private static string CoalesceHistoryValue(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
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

        var draftSnapshot = CloneDraftForHistoryPersistence(draft);
        _ = PersistHistoryEntryAsync(draftSnapshot, responseText, actionType);
    }

    private async Task PersistHistoryEntryAsync(OrderDraft draftSnapshot, string responseText, string actionType)
    {
        await _historyPersistenceGate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                var entry = new OrderHistoryEntry
                {
                    DraftId = draftSnapshot.DraftId,
                    OrderNumber = draftSnapshot.OrderNumber,
                    SessionId = draftSnapshot.SessionId,
                    Timestamp = DateTime.Now,
                    ReceiverName = draftSnapshot.ReceiverName,
                    ReceiverMobile = draftSnapshot.ReceiverMobile,
                    ReceiverAddress = draftSnapshot.ReceiverAddress,
                    GoodsSummary = draftSnapshot.GoodsSummary,
                    Status = draftSnapshot.Status,
                    StatusDetail = draftSnapshot.StatusDetail,
                    OperatorLoginName = draftSnapshot.OperatorLoginName,
                    OperatorErpId = draftSnapshot.OperatorErpId,
                    BusinessGroupId = draftSnapshot.BusinessGroupId,
                    BusinessGroupName = draftSnapshot.BusinessGroupName,
                    RawText = draftSnapshot.RawText,
                    ResponseText = responseText
                };

                _historyRepository.Upsert(entry);
                _auditRepository.Append(new OrderAuditRecord
                {
                    RecordId = $"{draftSnapshot.DraftId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    DraftId = draftSnapshot.DraftId,
                    OrderNumber = draftSnapshot.OrderNumber,
                    SessionId = draftSnapshot.SessionId,
                    Timestamp = DateTime.Now,
                    ActionType = actionType,
                    ReceiverName = draftSnapshot.ReceiverName,
                    ReceiverMobile = draftSnapshot.ReceiverMobile,
                    ReceiverAddress = draftSnapshot.ReceiverAddress,
                    GoodsSummary = draftSnapshot.GoodsSummary,
                    Status = draftSnapshot.Status,
                    OperatorLoginName = draftSnapshot.OperatorLoginName,
                    OperatorErpId = draftSnapshot.OperatorErpId,
                    BusinessGroupId = draftSnapshot.BusinessGroupId,
                    BusinessGroupName = draftSnapshot.BusinessGroupName,
                    RawText = draftSnapshot.RawText,
                    SnapshotJson = BuildDraftSnapshotJson(draftSnapshot),
                    ResponseText = responseText
                });
            });
        }
        catch
        {
        }
        finally
        {
            _historyPersistenceGate.Release();
        }

        _historyRefreshPending = true;
    }

    private async Task PersistUpdatedHistoryEntriesAsync(IEnumerable<OrderAuditRecord> entries)
    {
        var records = entries.ToList();
        if (records.Count == 0)
        {
            return;
        }

        await _historyPersistenceGate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                foreach (var record in records)
                {
                    _auditRepository.Upsert(record);
                }
            });
        }
        catch
        {
        }
        finally
        {
            _historyPersistenceGate.Release();
        }
    }

    private static OrderDraft CloneDraftForHistoryPersistence(OrderDraft draft)
    {
        return new OrderDraft
        {
            DraftId = draft.DraftId,
            OrderNumber = draft.OrderNumber,
            SessionId = draft.SessionId,
            OrderIndex = draft.OrderIndex,
            RawText = draft.RawText,
            ReceiverName = draft.ReceiverName,
            ReceiverMobile = draft.ReceiverMobile,
            ReceiverProvince = draft.ReceiverProvince,
            ReceiverCity = draft.ReceiverCity,
            ReceiverArea = draft.ReceiverArea,
            ReceiverRegion = draft.ReceiverRegion,
            ReceiverAddress = draft.ReceiverAddress,
            Remark = draft.Remark,
            HasGift = draft.HasGift,
            OperatorLoginName = draft.OperatorLoginName,
            OperatorErpId = draft.OperatorErpId,
            BusinessGroupId = draft.BusinessGroupId,
            BusinessGroupName = draft.BusinessGroupName,
            Status = draft.Status,
            StatusDetail = draft.StatusDetail,
            ParseWarnings = draft.ParseWarnings,
            IsBatchUploadSelected = draft.IsBatchUploadSelected,
            Items = new ObservableCollection<OrderItemDraft>(draft.Items.Select(item => new OrderItemDraft
            {
                SequenceNumber = item.SequenceNumber,
                SourceText = item.SourceText,
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                SpecCodeText = item.SpecCodeText,
                BarcodeText = item.BarcodeText,
                WearPeriod = item.WearPeriod,
                QuantityText = item.QuantityText,
                Remark = item.Remark,
                DegreeText = item.DegreeText,
                IsTrial = item.IsTrial,
                IsOutOfStock = item.IsOutOfStock,
                MatchHint = item.MatchHint,
                UseManualProductCodeStyle = item.UseManualProductCodeStyle
            }))
        };
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
            draft.ReceiverProvince,
            draft.ReceiverCity,
            draft.ReceiverArea,
            draft.ReceiverRegion,
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
                item.SpecCodeText,
                item.BarcodeText,
                item.WearPeriod,
                item.QuantityText,
                item.Remark,
                item.DegreeText,
                item.IsTrial,
                item.IsOutOfStock,
                item.MatchHint,
                item.UseManualProductCodeStyle
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
        RenumberDraftItems(_selectedDraft);
        GridDraftOrders.Items.Refresh();
        GridDraftItems.Items.Refresh();
        TxtQueueSummary.Text = _draftOrders.Count == 0
            ? "当前还没有解析结果。"
            : $"共 {_draftOrders.Count} 条订单，其中上传成功 {_draftOrders.Count(item => item.Status == "上传成功")} 条。";
        UpdateWorkbenchState();
    }

    private static void RenumberDraftItems(OrderDraft? draft)
    {
        if (draft is null || draft.Items.Count == 0)
        {
            return;
        }

        for (var index = 0; index < draft.Items.Count; index++)
        {
            draft.Items[index].SequenceNumber = index + 1;
        }
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
            RenumberDraftItems(draft);
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
            UpdateProductWorkflowPanel(null);
            return;
        }

        TxtCurrentDraftHeadline.Text = $"{DisplayValue(draft.OrderNumber, $"订单 #{draft.OrderIndex}")} · {draft.Status}";
        TxtCurrentDraftMeta.Text =
            $"{DisplayValue(draft.ReceiverName, "未填写收件人")} / {DisplayValue(draft.ReceiverMobile, "未填写联系电话")} / 商品 {draft.Items.Count} 项 / 编号 {DisplayValue(draft.OrderNumber, "待生成")}";
        UpdateProductWorkflowPanel(draft);
    }

    private void UpdateProductWorkflowPanel(OrderDraft? draft)
    {
        if (draft is null || draft.Items.Count == 0)
        {
            SetTextBlockMessage(
                TxtProductWorkflowSummary,
                draft is null
                    ? "商品编码进度：先从左侧队列选择一条订单。"
                    : "商品编码进度：当前订单还没有商品，请先补充商品项。",
                WorkflowNeutralBrush);
            SetTextBlockMessage(
                TxtProductWorkflowHint,
                "系统会按 周期 / 型号 / 度数 自动尝试匹配商品编码；有待处理或缺货项时，这里会直接列出对应序号。",
                WorkflowMutedBrush);
            return;
        }

        var outOfStockItems = draft.Items
            .Where(item => item.IsOutOfStock)
            .OrderBy(item => item.SequenceNumber)
            .ToList();
        var exactCount = draft.Items.Count(item =>
            !item.IsOutOfStock &&
            string.Equals(item.ProductMatchState, "Exact", StringComparison.OrdinalIgnoreCase));
        var partialCount = draft.Items.Count(item =>
            !item.IsOutOfStock &&
            string.Equals(item.ProductMatchState, "Partial", StringComparison.OrdinalIgnoreCase));
        var unmatchedCount = draft.Items.Count(item =>
            !item.IsOutOfStock &&
            string.Equals(item.ProductMatchState, "Unmatched", StringComparison.OrdinalIgnoreCase));
        var confirmedCount = draft.Items.Count(item => item.ProductCodeConfirmed && !item.IsOutOfStock);
        var pendingMatchItems = draft.Items
            .Where(item =>
                !item.IsOutOfStock &&
                !string.Equals(item.ProductMatchState, "Exact", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.SequenceNumber)
            .ToList();
        var unconfirmedItems = draft.Items
            .Where(item =>
                !item.IsOutOfStock &&
                item.ProductCodeConfirmed == false &&
                string.Equals(item.ProductMatchState, "Exact", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.SequenceNumber)
            .ToList();

        RenderProductWorkflowSummary(draft.Items.Count, exactCount, partialCount, unmatchedCount, confirmedCount, pendingMatchItems.Count, outOfStockItems.Count);
        RenderProductWorkflowHint(outOfStockItems, pendingMatchItems, unconfirmedItems);
    }

    private void RenderProductWorkflowSummary(
        int totalCount,
        int exactCount,
        int partialCount,
        int unmatchedCount,
        int confirmedCount,
        int pendingCount,
        int outOfStockCount)
    {
        TxtProductWorkflowSummary.Inlines.Clear();
        TxtProductWorkflowSummary.Inlines.Add(new Run("商品编码进度：") { Foreground = WorkflowNeutralBrush, FontWeight = FontWeights.SemiBold });
        TxtProductWorkflowSummary.Inlines.Add(new Run($"共 {totalCount} 项，") { Foreground = WorkflowNeutralBrush });
        TxtProductWorkflowSummary.Inlines.Add(new Run($"完全匹配 {exactCount} 项") { Foreground = WorkflowSuccessBrush, FontWeight = FontWeights.SemiBold });
        TxtProductWorkflowSummary.Inlines.Add(new Run("，") { Foreground = WorkflowNeutralBrush });
        TxtProductWorkflowSummary.Inlines.Add(new Run($"待处理 {pendingCount} 项") { Foreground = pendingCount > 0 ? WorkflowDangerBrush : WorkflowSuccessBrush, FontWeight = FontWeights.SemiBold });
        TxtProductWorkflowSummary.Inlines.Add(new Run("，") { Foreground = WorkflowNeutralBrush });
        TxtProductWorkflowSummary.Inlines.Add(new Run($"缺货 {outOfStockCount} 项") { Foreground = outOfStockCount > 0 ? WorkflowStockBrush : WorkflowSuccessBrush, FontWeight = FontWeights.SemiBold });
        TxtProductWorkflowSummary.Inlines.Add(new Run($"，部分匹配 {partialCount} 项，未匹配 {unmatchedCount} 项，已确认 {confirmedCount} 项。") { Foreground = WorkflowNeutralBrush });
    }

    private void RenderProductWorkflowHint(
        IReadOnlyList<OrderItemDraft> outOfStockItems,
        IReadOnlyList<OrderItemDraft> pendingMatchItems,
        IReadOnlyList<OrderItemDraft> unconfirmedItems)
    {
        TxtProductWorkflowHint.Inlines.Clear();
        var hasHint = false;
        if (outOfStockItems.Count > 0)
        {
            TxtProductWorkflowHint.Inlines.Add(new Run("缺货待人工确认序号：") { Foreground = WorkflowMutedBrush });
            AppendSequenceLinks(TxtProductWorkflowHint, outOfStockItems, WorkflowStockBrush);
            TxtProductWorkflowHint.Inlines.Add(new Run("。点击蓝色序号可直接定位到对应商品。") { Foreground = WorkflowStockBrush });
            hasHint = true;
        }

        if (pendingMatchItems.Count > 0)
        {
            if (hasHint)
            {
                TxtProductWorkflowHint.Inlines.Add(new Run("  ") { Foreground = WorkflowMutedBrush });
            }

            TxtProductWorkflowHint.Inlines.Add(new Run("待优先处理序号：") { Foreground = WorkflowMutedBrush });
            AppendSequenceLinks(TxtProductWorkflowHint, pendingMatchItems, WorkflowDangerBrush);
            TxtProductWorkflowHint.Inlines.Add(new Run("。点击红色序号可直接定位到对应商品。") { Foreground = WorkflowDangerBrush });
            hasHint = true;
        }

        if (unconfirmedItems.Count > 0)
        {
            if (hasHint)
            {
                TxtProductWorkflowHint.Inlines.Add(new Run("  ") { Foreground = WorkflowMutedBrush });
            }

            TxtProductWorkflowHint.Inlines.Add(new Run("以下序号已匹配但还没人工确认：") { Foreground = WorkflowMutedBrush });
            AppendSequenceLinks(TxtProductWorkflowHint, unconfirmedItems, WorkflowNeutralBrush);
            TxtProductWorkflowHint.Inlines.Add(new Run("。点击序号可直接定位。") { Foreground = WorkflowMutedBrush });
            hasHint = true;
        }

        if (!hasHint)
        {
            TxtProductWorkflowHint.Inlines.Add(new Run("所有商品都已完全匹配并确认，可以直接校验并上传。") { Foreground = WorkflowSuccessBrush });
        }
    }

    private void AppendSequenceLinks(TextBlock target, IReadOnlyList<OrderItemDraft> items, Brush foreground)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (index > 0)
            {
                target.Inlines.Add(new Run("、") { Foreground = foreground });
            }

            var item = items[index];
            var link = new Hyperlink(new Run(item.SequenceNumber.ToString()))
            {
                Foreground = foreground,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Tag = item.SequenceNumber
            };
            link.Click += ProductWorkflowSequenceLink_Click;
            target.Inlines.Add(link);
        }
    }

    private void ProductWorkflowSequenceLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink { Tag: int sequenceNumber })
        {
            return;
        }

        var targetItem = _selectedDraft?.Items.FirstOrDefault(item => item.SequenceNumber == sequenceNumber);
        if (targetItem is null)
        {
            return;
        }

        GridDraftItems.SelectedItem = targetItem;
        GridDraftItems.ScrollIntoView(targetItem);
        TxtStatus.Text = targetItem.IsOutOfStock
            ? $"已定位到商品序号 {sequenceNumber}，当前商品编码缺货，待人工确认。"
            : $"已定位到商品序号 {sequenceNumber}。";
    }

    private void UpdateActionAvailability()
    {
        var hasDrafts = _draftOrders.Count > 0;
        var hasSelectedDraft = _selectedDraft is not null;
        var hasSourceInput = !string.IsNullOrWhiteSpace(TxtInput.Text);
        var hasSearchKeyword = !string.IsNullOrWhiteSpace(TxtSearchKeyword.Text);
        var hasSearchMatches = _sourceSearchMatchIndexes.Count > 1;
        var hasRunnableDraft = _draftOrders.Any(item =>
            !string.Equals(item.Status, "上传成功", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(item.Status, "已跳过", StringComparison.OrdinalIgnoreCase));
        var hasSelectedRunnableDraft = _draftOrders.Any(item =>
            item.IsBatchUploadSelected &&
            !string.Equals(item.Status, "上传成功", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(item.Status, "已跳过", StringComparison.OrdinalIgnoreCase));

        BtnParse.IsEnabled = !_isParsing;
        BtnSearch.IsEnabled = hasSourceInput && hasSearchKeyword;
        BtnSearchPrev.IsEnabled =  hasSourceInput && hasSearchKeyword && hasSearchMatches;
        BtnSearchNext.IsEnabled =  hasSourceInput && hasSearchKeyword && hasSearchMatches;
        //BtnSaveParseRecord.IsEnabled = !_isParsing && hasDrafts;
        BtnValidateCurrent.IsEnabled = !_isParsing && hasSelectedDraft;
        BtnSubmitCurrent.IsEnabled = !_isParsing && hasSelectedDraft;
        BtnSkipCurrent.IsEnabled = !_isParsing && hasSelectedDraft;
        BtnAddItem.IsEnabled = !_isParsing && hasSelectedDraft;
        BtnRemoveItem.IsEnabled = !_isParsing && hasSelectedDraft && GridDraftItems.SelectedItem is OrderItemDraft;
        BtnSubmitAll.IsEnabled = !_isParsing && hasRunnableDraft && hasSelectedRunnableDraft;
        BtnQueryTradeRecords.IsEnabled = !_isParsing;
        BtnQueryGoodsCodes.IsEnabled = !_isParsing;
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

    private static void ApplyProductCodeComboFilter(ComboBox comboBox, string? keyword)
    {
        if (comboBox.ItemsSource is null)
        {
            return;
        }

        var normalizedKeyword = ProductCodeSearchHelper.NormalizeKeyword(keyword);
        var view = CollectionViewSource.GetDefaultView(comboBox.ItemsSource);
        view.Filter = item => item is ProductCodeOption option && ProductCodeSearchHelper.Matches(option, normalizedKeyword);
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

    private static string BuildGoodsCodeQueryDisplayText(HupunUploadAttemptResult result, OrderItemDraft targetItem)
    {
        var lines = new List<string>
        {
            $"source_text: {DisplayValue(targetItem.SourceText, "无")}",
            $"query_spec_code: {DisplayValue(targetItem.SpecCodeText, "无")}",
            $"query_item_code: {DisplayValue(targetItem.ProductCode, "无")}",
            $"query_bar_code: {DisplayValue(targetItem.BarcodeText, "无")}"
        };

        if (!string.IsNullOrWhiteSpace(result.FriendlyMessage))
        {
            lines.Add($"message: {result.FriendlyMessage}");
        }

        var goodsTable = BuildGoodsQueryTable(result.ResponseText, maxRows: 30);
        if (!string.IsNullOrWhiteSpace(goodsTable))
        {
            lines.Add(string.Empty);
            lines.Add("goods_table(top30):");
            lines.Add(goodsTable);
        }

        lines.Add(string.Empty);
        lines.Add(result.DebugText);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildFullGoodsCodeQueryDisplayText(HupunUploadAttemptResult result)
    {
        var lines = new List<string>
        {
            "query_mode: full_catalog_by_modify_time",
            $"modify_time: {DisplayValue(ReadRequestField(result, "modify_time"), "无")}",
            $"end_time: {DisplayValue(ReadRequestField(result, "end_time"), "无")}",
            $"page_range: 1 - {DisplayValue(ReadRequestField(result, "page_end"), "0")}",
            $"limit: {DisplayValue(ReadRequestField(result, "limit"), "无")}"
        };

        if (!string.IsNullOrWhiteSpace(result.FriendlyMessage))
        {
            lines.Add($"message: {result.FriendlyMessage}");
        }

        var goodsTable = BuildGoodsQueryTable(result.ResponseText, maxRows: 30);
        if (!string.IsNullOrWhiteSpace(goodsTable))
        {
            lines.Add(string.Empty);
            lines.Add("goods_table(top30):");
            lines.Add(goodsTable);
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

    private static string BuildGoodsQueryTable(string responseText, int maxRows)
    {
        if (!TryGetGoodsQueryData(responseText, out var data))
        {
            return string.Empty;
        }

        var rows = new List<IReadOnlyList<string>>();
        foreach (var goods in data.EnumerateArray().Take(Math.Max(1, maxRows)))
        {
            var specsSummary = string.Empty;
            if (goods.TryGetProperty("specs", out var specsElement) && specsElement.ValueKind == JsonValueKind.Array)
            {
                specsSummary = string.Join(", ",
                    specsElement.EnumerateArray()
                        .Take(3)
                        .Select(spec =>
                        {
                            var code = ReadJsonText(spec, "spec_code");
                            var name = ReadJsonText(spec, "spec_name");
                            return string.IsNullOrWhiteSpace(name) ? code : $"{code}/{name}";
                        })
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            rows.Add(new[]
            {
                ReadJsonText(goods, "goods_code"),
                ReadJsonText(goods, "goods_name"),
                ReadJsonText(goods, "brand_name"),
                ReadJsonText(goods, "status"),
                ReadJsonText(goods, "unit_name"),
                ReadJsonText(goods, "modify_time"),
                specsSummary
            });
        }

        if (rows.Count == 0)
        {
            return string.Empty;
        }

        return BuildPlainTextTable(
            new[] { "goods_code", "goods_name", "brand", "status", "unit", "modify_time", "specs(top3)" },
            rows,
            maxColumnWidth: 28);
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

    private static bool TryGetGoodsQueryData(string responseText, out JsonElement dataElement)
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

    private static string ReadRequestField(HupunUploadAttemptResult result, string key)
    {
        return result.RequestFields.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private readonly record struct TradeQuerySummary(int Count, IReadOnlyList<string> TradeNumbers, IReadOnlyList<string> Uids);

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

    private static void SetTextBlockMessage(TextBlock target, string message, Brush foreground)
    {
        target.Inlines.Clear();
        target.Inlines.Add(new Run(message) { Foreground = foreground });
    }

    private static Brush CreateFrozenBrush(string colorText)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colorText)!;
        brush.Freeze();
        return brush;
    }

    private static string Safe(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static string DisplayValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private readonly record struct HistoryServerSyncSummary(int UploadedCount, int FailedCount)
    {
        public static HistoryServerSyncSummary Empty => new(0, 0);
    }

    private readonly record struct HistoryPageResult(
        int TotalCount,
        int PageNumber,
        List<OrderAuditRecord> Entries)
    {
        public static HistoryPageResult Empty => new(0, 1, new List<OrderAuditRecord>());
    }

    private readonly record struct ServerOrderState(bool HasSuccessfulUpload, bool HasSuccessfulCancellation, int RecordCount)
    {
        public static ServerOrderState Empty => new(false, false, 0);
    }

    private sealed class HistoryDraftSnapshot
    {
        public string? DraftId { get; set; }

        public string? OrderNumber { get; set; }

        public string? SessionId { get; set; }

        public int OrderIndex { get; set; }

        public string? RawText { get; set; }

        public string? Status { get; set; }

        public string? StatusDetail { get; set; }

        public string? ReceiverName { get; set; }

        public string? ReceiverMobile { get; set; }

        public string? ReceiverProvince { get; set; }

        public string? ReceiverCity { get; set; }

        public string? ReceiverArea { get; set; }

        public string? ReceiverRegion { get; set; }

        public string? ReceiverAddress { get; set; }

        public string? Remark { get; set; }

        public bool HasGift { get; set; }

        public string? OperatorLoginName { get; set; }

        public string? OperatorErpId { get; set; }

        public long? BusinessGroupId { get; set; }

        public string? BusinessGroupName { get; set; }

        public List<HistoryDraftSnapshotItem> Items { get; set; } = new();
    }

    private sealed class HistoryDraftSnapshotItem
    {
        public string? SourceText { get; set; }

        public string? ProductCode { get; set; }

        public string? ProductName { get; set; }

        public string? SpecCodeText { get; set; }

        public string? BarcodeText { get; set; }

        public string? WearPeriod { get; set; }

        public string? QuantityText { get; set; }

        public string? Remark { get; set; }

        public string? DegreeText { get; set; }

        public bool IsTrial { get; set; }

        public bool IsOutOfStock { get; set; }

        public string? MatchHint { get; set; }

        public bool UseManualProductCodeStyle { get; set; }
    }

    private sealed record ParseTaskResult(
        IReadOnlyList<OrderDraft> Drafts,
        ParseResult ParseResult,
        IReadOnlyList<Task> PendingUiTasks);
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
