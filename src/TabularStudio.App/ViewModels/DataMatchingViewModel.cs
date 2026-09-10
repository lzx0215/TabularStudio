using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TabularStudio.App.Dialogs;
using TabularStudio.App.Models;
using TabularStudio.App.Services;
using TabularStudio.Core.Contracts;

namespace TabularStudio.App.ViewModels;

public sealed partial class DataMatchingViewModel : ObservableObject
{
    private readonly IWorkbookInspectionService _inspectionService;
    private readonly IDataMatchingService _matchingService;
    private readonly Func<string, ExistingOutputChoice>? _confirmExistingOutput;
    private readonly Func<string, string?>? _showSaveFileDialog;
    private readonly Func<string?>? _showOpenFileDialog;
    private readonly IOutputDirectoryPreferenceService _outputDirectoryService;
    private readonly Func<string, string?, string?>? _showSaveFileDialogWithOptions;

    private bool _suppressMasterPreviewRefresh;
    private bool _suppressReferencePreviewRefresh;

    private int _masterFileLoadGeneration;
    private int _masterPreviewGeneration;

    private int _referenceFileLoadGeneration;
    private int _referencePreviewGeneration;

    private string? _errorSource;

    // 跨功能页面流转 (Pending Master Handoff)
    [ObservableProperty]
    private string? _pendingMasterFilePath;

    [ObservableProperty]
    private bool _hasPendingMaster;

    // 状态枚举
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigure))]
    [NotifyPropertyChangedFor(nameof(CanUseSameFile))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanExecuteSuccessActions))]
    private DataMatchingPageState _state = DataMatchingPageState.Initial;

    // 主表配置
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private string? _masterFilePath;

    public bool IsMasterCsv => TabularFileTypes.IsCsv(MasterFilePath);
    public bool HasMasterWorksheetSelector => !IsMasterCsv;
    public bool CanUseSameFile => CanConfigure && !IsMasterCsv;
    partial void OnMasterFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(IsMasterCsv));
        OnPropertyChanged(nameof(HasMasterWorksheetSelector));
        OnPropertyChanged(nameof(CanUseSameFile));
        if (IsMasterCsv) UseSameFileAsMaster = false;
    }

    public ObservableCollection<WorksheetInfo> MasterWorksheets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private WorksheetInfo? _selectedMasterWorksheet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private int _masterHeaderRowNumber = 1;

    [ObservableProperty]
    private DataTable? _masterPreviewDataTable;

    [ObservableProperty]
    private int _masterPreviewRowCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMasterPreviewEmptyNotice))]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private bool _hasMasterPreviewData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isMasterPreviewLoading;

    [ObservableProperty]
    private string _masterPreviewEmptyMessage = "请先选择主表 Excel 文件以预览数据";

    public ObservableCollection<AvailableColumnItem> MasterAvailableColumns { get; } = [];

    // 对照表配置
    [ObservableProperty]
    private bool _useSameFileAsMaster;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private string? _referenceFilePath;

    public bool IsReferenceCsv => TabularFileTypes.IsCsv(ReferenceFilePath);
    public bool HasReferenceWorksheetSelector => !IsReferenceCsv;
    partial void OnReferenceFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(IsReferenceCsv));
        OnPropertyChanged(nameof(HasReferenceWorksheetSelector));
    }

    public ObservableCollection<WorksheetInfo> ReferenceWorksheets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private WorksheetInfo? _selectedReferenceWorksheet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private int _referenceHeaderRowNumber = 1;

    [ObservableProperty]
    private DataTable? _referencePreviewDataTable;

    [ObservableProperty]
    private int _referencePreviewRowCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReferencePreviewEmptyNotice))]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    private bool _hasReferencePreviewData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isReferencePreviewLoading;

    [ObservableProperty]
    private string _referencePreviewEmptyMessage = "请先选择对照表 Excel 文件以预览数据";

    public ObservableCollection<AvailableColumnItem> ReferenceAvailableColumns { get; } = [];

    // 匹配条件配置 (1～N 条)
    public ObservableCollection<MatchingConditionRowViewModel> Conditions { get; } = [];

    // 带回字段列表 (从对照表)
    public ObservableCollection<ReturnFieldItemViewModel> ReturnFields { get; } = [];

    [ObservableProperty]
    private string? _searchReturnFieldText;

    // 匹配选项
    [ObservableProperty]
    private bool _normalizeComparisonKeys = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isMasterFilterEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private AvailableColumnItem? _selectedMasterFilterColumn;

    public ObservableCollection<ColumnValueOption> MasterFilterValues { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private ColumnValueOption? _selectedMasterFilterValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectMasterFilterValue))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isMasterFilterValuesLoading;

    [ObservableProperty]
    private string? _masterFilterValuesMessage;

    public bool CanSelectMasterFilterValue => !IsMasterFilterValuesLoading;
    public Task MasterFilterValuesLoadTask { get; private set; } = Task.CompletedTask;
    private CancellationTokenSource? _masterFilterValuesCancellation;
    private int _masterFilterValuesGeneration;

    public bool HasValidMasterFilter => !IsMasterFilterEnabled ||
        (SelectedMasterFilterColumn is not null && MasterAvailableColumns.Contains(SelectedMasterFilterColumn)
            && !IsMasterFilterValuesLoading && SelectedMasterFilterValue is not null
            && MasterFilterValues.Contains(SelectedMasterFilterValue));

    [ObservableProperty]
    private int _resultSkippedCount;

    [ObservableProperty]
    private bool _isStatusColumnEnabled = true;

    [ObservableProperty]
    private string _statusColumnName = "匹配状态";

    // 输出路径
    [ObservableProperty]
    private string? _outputFilePath;

    [ObservableProperty]
    private bool _isUserSpecifiedOutputPath;

    [ObservableProperty]
    private bool _hasOutputConflictWithInput;

    [ObservableProperty]
    private string? _outputConflictMessage;

    // 执行状态与进度
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanConfigure))]
    [NotifyPropertyChangedFor(nameof(CanUseSameFile))]
    [NotifyPropertyChangedFor(nameof(CanBrowseMaster))]
    [NotifyPropertyChangedFor(nameof(CanBrowseReference))]
    [NotifyPropertyChangedFor(nameof(CanSelectProfile))]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    [NotifyPropertyChangedFor(nameof(CanDeleteProfile))]
    private bool _isProcessing;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string? _progressStageText;

    // 成功状态指标 (固定五项统计)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecuteSuccessActions))]
    private string? _resultOutputFilePath;

    [ObservableProperty]
    private int _resultTotalMasterDataRowCount;

    [ObservableProperty]
    private int _resultMatchedCount;

    [ObservableProperty]
    private int _resultUnmatchedCount;

    [ObservableProperty]
    private int _resultDuplicateCount;

    [ObservableProperty]
    private int _resultEmptyKeyCount;

    [ObservableProperty]
    private TimeSpan _resultElapsed;

    [ObservableProperty]
    private string? _successMessage;

    [ObservableProperty]
    private bool _hasSuccess;

    // 错误状态
    [ObservableProperty]
    private string? _errorTitle;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorDetail))]
    private string? _errorDetail;

    [ObservableProperty]
    private bool _hasError;

    private readonly IProcessingProfileStore? _profileStore;
    private readonly IProcessingProfileValidator? _profileValidator;
    private readonly Func<string, string?>? _promptProfileName;
    private readonly Func<string, bool>? _confirmOverwrite;
    private readonly Func<string, bool>? _confirmDelete;

    private string? _appliedProfileName;
    private bool _isApplyingProfile;
    private bool _suppressFilterColumnLoad;
    private int _profileValidationGeneration;

    public ObservableCollection<ProcessingProfile> Profiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanDeleteProfile))]
    private ProcessingProfile? _selectedProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileStatusError))]
    [NotifyPropertyChangedFor(nameof(IsProfileStatusModified))]
    [NotifyPropertyChangedFor(nameof(IsProfileStatusSuccess))]
    private string _profileStatusMessage = "暂无保存配置";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigure))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanSelectProfile))]
    [NotifyPropertyChangedFor(nameof(CanApplyProfile))]
    [NotifyPropertyChangedFor(nameof(CanSaveProfile))]
    [NotifyPropertyChangedFor(nameof(CanDeleteProfile))]
    [NotifyPropertyChangedFor(nameof(CanBrowseMaster))]
    [NotifyPropertyChangedFor(nameof(CanBrowseReference))]
    private bool _isProfileValidating;

    public bool HasProfiles => Profiles.Count > 0;
    public bool CanSelectProfile => !IsProcessing && !IsProfileValidating;
    public bool CanApplyProfile =>
        !IsProcessing &&
        !IsProfileValidating &&
        SelectedProfile is not null &&
        !string.IsNullOrWhiteSpace(MasterFilePath) &&
        (IsMasterCsv || SelectedMasterWorksheet != null) &&
        MasterHeaderRowNumber >= 1 &&
        HasMasterPreviewData &&
        !IsMasterPreviewLoading &&
        !string.IsNullOrWhiteSpace(ReferenceFilePath) &&
        (IsReferenceCsv || SelectedReferenceWorksheet != null) &&
        ReferenceHeaderRowNumber >= 1 &&
        HasReferencePreviewData &&
        !IsReferencePreviewLoading;

    public bool CanSaveProfile =>
        !IsProcessing &&
        !IsProfileValidating &&
        !string.IsNullOrWhiteSpace(MasterFilePath) &&
        (IsMasterCsv || SelectedMasterWorksheet != null) &&
        MasterHeaderRowNumber >= 1 &&
        HasMasterPreviewData &&
        !IsMasterPreviewLoading &&
        !string.IsNullOrWhiteSpace(ReferenceFilePath) &&
        (IsReferenceCsv || SelectedReferenceWorksheet != null) &&
        ReferenceHeaderRowNumber >= 1 &&
        HasReferencePreviewData &&
        !IsReferencePreviewLoading &&
        Conditions.Count >= 1 &&
        Conditions.All(c => c.SelectedMasterColumn != null &&
                            MasterAvailableColumns.Contains(c.SelectedMasterColumn) &&
                            c.SelectedReferenceColumn != null &&
                            ReferenceAvailableColumns.Contains(c.SelectedReferenceColumn)) &&
        ReturnFields.Any(f => f.IsSelected && ReferenceAvailableColumns.Any(r => r.Reference == f.Reference)) &&
        ReturnFields.Where(f => f.IsSelected).All(f => ReferenceAvailableColumns.Any(r => r.Reference == f.Reference)) &&
        HasValidMasterFilter &&
        (!IsStatusColumnEnabled || !string.IsNullOrWhiteSpace(StatusColumnName));

    public bool CanDeleteProfile => !IsProcessing && !IsProfileValidating && SelectedProfile is not null;

    public bool IsProfileStatusError => ProfileStatusMessage.StartsWith("未应用") || ProfileStatusMessage.Contains("失败");
    public bool IsProfileStatusModified => ProfileStatusMessage == "当前设置已修改";
    public bool IsProfileStatusSuccess => ProfileStatusMessage.StartsWith("已应用") || ProfileStatusMessage.StartsWith("已保存");

    // 派生属性
    public bool CanConfigure => !IsProcessing && !IsProfileValidating && State != DataMatchingPageState.Initial;

    public bool CanStart =>
        (State is DataMatchingPageState.Ready or DataMatchingPageState.Success ||
            (State == DataMatchingPageState.Error && _errorSource == "Execute")) &&
        !IsProcessing &&
        !IsProfileValidating &&
        HasValidMasterFilter &&
        !string.IsNullOrWhiteSpace(MasterFilePath) &&
        (IsMasterCsv || SelectedMasterWorksheet != null) &&
        MasterHeaderRowNumber >= 1 &&
        HasMasterPreviewData &&
        !string.IsNullOrWhiteSpace(ReferenceFilePath) &&
        (IsReferenceCsv || SelectedReferenceWorksheet != null) &&
        ReferenceHeaderRowNumber >= 1 &&
        HasReferencePreviewData &&
        Conditions.Count >= 1 &&
        Conditions.All(c => c.SelectedMasterColumn != null && c.SelectedReferenceColumn != null) &&
        ReturnFields.Any(f => f.IsSelected) &&
        (!IsStatusColumnEnabled || !string.IsNullOrWhiteSpace(StatusColumnName)) &&
        !string.IsNullOrWhiteSpace(OutputFilePath) &&
        !HasOutputConflictWithInput &&
        !IsMasterPreviewLoading &&
        !IsReferencePreviewLoading;

    public bool CanExecuteSuccessActions => State == DataMatchingPageState.Success && !string.IsNullOrWhiteSpace(ResultOutputFilePath);

    public bool CanBrowseMaster => !IsProcessing && !IsProfileValidating;

    public bool CanBrowseReference => !IsProcessing && !IsProfileValidating && !UseSameFileAsMaster;

    public bool ShowMasterPreviewEmptyNotice => !HasMasterPreviewData;

    public bool ShowReferencePreviewEmptyNotice => !HasReferencePreviewData;

    public bool HasErrorDetail => !string.IsNullOrWhiteSpace(ErrorDetail);

    public int SelectedReturnFieldsCount => ReturnFields.Count(f => f.IsSelected);

    public int TotalReturnFieldsCount => ReturnFields.Count;

    public DataMatchingViewModel(
        IWorkbookInspectionService inspectionService,
        IDataMatchingService matchingService,
        Func<string, ExistingOutputChoice>? confirmExistingOutput = null,
        Func<string, string?>? showSaveFileDialog = null,
        Func<string?>? showOpenFileDialog = null,
        IOutputDirectoryPreferenceService? outputDirectoryPreferenceService = null,
        Func<string, string?, string?>? showSaveFileDialogWithOptions = null,
        IProcessingProfileStore? profileStore = null,
        IProcessingProfileValidator? profileValidator = null,
        Func<string, string?>? promptProfileName = null,
        Func<string, bool>? confirmOverwrite = null,
        Func<string, bool>? confirmDelete = null)
    {
        _inspectionService = inspectionService ?? throw new ArgumentNullException(nameof(inspectionService));
        _matchingService = matchingService ?? throw new ArgumentNullException(nameof(matchingService));
        _confirmExistingOutput = confirmExistingOutput;
        _showSaveFileDialog = showSaveFileDialog;
        _showOpenFileDialog = showOpenFileDialog;
        _outputDirectoryService = outputDirectoryPreferenceService ?? new OutputDirectoryPreferenceService();
        _showSaveFileDialogWithOptions = showSaveFileDialogWithOptions;
        _profileStore = profileStore;
        _profileValidator = profileValidator;
        _promptProfileName = promptProfileName;
        _confirmOverwrite = confirmOverwrite;
        _confirmDelete = confirmDelete;

        // 默认初始化 1 条匹配条件
        AddInitialCondition();

        if (_profileStore != null)
        {
            RefreshProfiles();
        }
    }

    private void AddInitialCondition()
    {
        var row = new MatchingConditionRowViewModel(
            MasterAvailableColumns,
            ReferenceAvailableColumns,
            onDelete: RemoveCondition,
            onConditionChanged: OnConditionChanged);
        Conditions.Add(row);
        UpdateConditionRowIndicesAndCanDelete();
    }

    public void ReceiveMasterFilePath(string filePath)
    {
        PendingMasterFilePath = filePath;
        HasPendingMaster = !string.IsNullOrWhiteSpace(filePath);
        if (HasPendingMaster)
        {
            _ = LoadMasterFileAsync(filePath);
        }
    }

    #region Master File & Preview

    [RelayCommand]
    public async Task BrowseMasterFileAsync()
    {
        string? selectedPath = _showOpenFileDialog != null
            ? _showOpenFileDialog()
            : DefaultShowOpenFileDialog();

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            await LoadMasterFileAsync(selectedPath);
        }
    }

    public async Task LoadMasterFileAsync(string filePath)
    {
        int currentLoadGeneration = ++_masterFileLoadGeneration;
        ++_masterPreviewGeneration;
        InvalidateMasterPreview();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex)
        {
            if (currentLoadGeneration != _masterFileLoadGeneration)
            {
                return;
            }

            SetError("主表文件路径无效", "所选主表文件路径格式不正确。", ex.Message, "Master");
            State = DataMatchingPageState.Error;
            return;
        }

        string extension = Path.GetExtension(fullPath);
        if (!TabularFileTypes.IsSupported(fullPath))
        {
            if (currentLoadGeneration != _masterFileLoadGeneration)
            {
                return;
            }

            SetError("不支持的主表文件格式", "支持 .xlsx、.xls、.csv 格式文件。", fullPath, "Master");
            State = DataMatchingPageState.Error;
            return;
        }

        // 立即提交新主表加载状态，清除旧主表状态
        if (_errorSource == "Master" || _errorSource == "Execute")
        {
            ClearError();
        }
        ResetSuccess();

        MasterFilePath = fullPath;
        MasterWorksheets.Clear();
        SelectedMasterWorksheet = null;
        MasterHeaderRowNumber = 1;
        MasterAvailableColumns.Clear();
        UpdateConditionMasterSelections();

        DeriveDefaultOutputPath();
        ValidateOutputPathConflict();
        UpdateReadyState();

        // 若同文件模式开启，对照表跟随最新主表
        if (UseSameFileAsMaster)
        {
            _ = LoadReferenceFileAsync(fullPath);
        }

        var inspectionResult = await _inspectionService.InspectAsync(new WorkbookInspectionRequest(fullPath));

        if (currentLoadGeneration != _masterFileLoadGeneration)
        {
            return;
        }

        if (!inspectionResult.Success)
        {
            MapOperationError(inspectionResult.Error, "Master");
            State = DataMatchingPageState.Error;
            return;
        }

        foreach (var ws in inspectionResult.Worksheets)
        {
            MasterWorksheets.Add(ws);
        }

        // 默认选中第 1 个 Sheet
        if (IsMasterCsv || MasterWorksheets.Count > 0)
        {
            _suppressMasterPreviewRefresh = true;
            SelectedMasterWorksheet = IsMasterCsv ? null : MasterWorksheets[0];
            _suppressMasterPreviewRefresh = false;

            await RefreshMasterPreviewAsync();
        }
    }

    private void InvalidateMasterPreview()
    {
        SelectedMasterFilterColumn = null;
        HasMasterPreviewData = false;
        MasterPreviewDataTable = null;
        MasterPreviewRowCount = 0;
        MasterAvailableColumns.Clear();
        UpdateConditionMasterSelections();
        if (State != DataMatchingPageState.Initial && State != DataMatchingPageState.Processing)
        {
            State = DataMatchingPageState.FileLoaded;
        }
        OnPropertyChanged(nameof(CanStart));
    }

    partial void OnSelectedMasterWorksheetChanged(WorksheetInfo? value)
    {
        if (_suppressMasterPreviewRefresh)
        {
            return;
        }

        ClearExecuteErrorIfPresent();
        ResetSuccess();
        InvalidateMasterPreview();
        _ = RefreshMasterPreviewAsync();
    }

    partial void OnMasterHeaderRowNumberChanged(int value)
    {
        if (_suppressMasterPreviewRefresh)
        {
            return;
        }

        ClearExecuteErrorIfPresent();
        ResetSuccess();
        InvalidateMasterPreview();
        _ = RefreshMasterPreviewAsync();
    }

    [RelayCommand]
    public void IncrementMasterHeaderRow()
    {
        MasterHeaderRowNumber++;
    }

    [RelayCommand]
    public void DecrementMasterHeaderRow()
    {
        if (MasterHeaderRowNumber > 1)
        {
            MasterHeaderRowNumber--;
        }
    }

    private async Task RefreshMasterPreviewAsync()
    {
        int currentGeneration = ++_masterPreviewGeneration;
        SelectedMasterFilterColumn = null;

        if (string.IsNullOrWhiteSpace(MasterFilePath) || (!IsMasterCsv && SelectedMasterWorksheet is null))
        {
            InvalidateMasterPreview();
            return;
        }

        if (MasterHeaderRowNumber < 1)
        {
            SetError("表头行号无效", "主表表头所在行必须是大于等于 1 的整数。", source: "Master");
            State = DataMatchingPageState.Error;
            return;
        }

        IsMasterPreviewLoading = true;
        MasterPreviewEmptyMessage = "正在读取主表预览数据...";

        try
        {
            var request = new WorksheetPreviewRequest(
                new WorksheetSource(MasterFilePath, SelectedMasterWorksheet?.Name, MasterHeaderRowNumber));

            var result = await _inspectionService.GetPreviewAsync(request);

            if (currentGeneration != _masterPreviewGeneration)
            {
                return;
            }

            if (!result.Success || result.Preview is null)
            {
                MasterPreviewDataTable = null;
                HasMasterPreviewData = false;
                MasterPreviewRowCount = 0;
                MasterPreviewEmptyMessage = "加载主表预览失败";
                MapOperationError(result.Error, "Master");
                State = DataMatchingPageState.Error;
                return;
            }

            var preview = result.Preview;
            var table = new DataTable();

            MasterAvailableColumns.Clear();
            for (int i = 0; i < preview.Columns.Count; i++)
            {
                var colRef = preview.Columns[i];
                string columnName = $"Col_{colRef.ColumnNumber}";
                string displayHeader = !string.IsNullOrWhiteSpace(colRef.HeaderText)
                    ? colRef.HeaderText
                    : $"第 {AvailableColumnItem.ToExcelColumnLetter(colRef.ColumnNumber)} 列";

                var col = table.Columns.Add(columnName, typeof(string));
                col.Caption = displayHeader;

                // 只有 HeaderText 非空的列才能作为匹配键
                if (!string.IsNullOrWhiteSpace(colRef.HeaderText))
                {
                    MasterAvailableColumns.Add(new AvailableColumnItem(colRef));
                }
            }

            foreach (var row in preview.Rows)
            {
                var dataRow = table.NewRow();
                for (int i = 0; i < row.Cells.Count && i < table.Columns.Count; i++)
                {
                    dataRow[i] = row.Cells[i].DisplayValue ?? string.Empty;
                }
                table.Rows.Add(dataRow);
            }

            MasterPreviewDataTable = table;
            MasterPreviewRowCount = table.Rows.Count;
            HasMasterPreviewData = true;
            MasterPreviewEmptyMessage = table.Rows.Count == 0 ? "工作表中没有数据行" : string.Empty;

            UpdateConditionMasterSelections();
            if (_errorSource == "Master")
            {
                ClearError();
            }
            UpdateReadyState();
        }
        catch (Exception ex)
        {
            if (currentGeneration != _masterPreviewGeneration)
            {
                return;
            }

            MasterPreviewDataTable = null;
            HasMasterPreviewData = false;
            MasterPreviewRowCount = 0;
            MasterPreviewEmptyMessage = "加载主表预览时发生异常";
            SetError("主表预览加载异常", "读取主表预览数据时出错。", ex.Message, "Master");
            State = DataMatchingPageState.Error;
        }
        finally
        {
            if (currentGeneration == _masterPreviewGeneration)
            {
                IsMasterPreviewLoading = false;
            }
        }
    }

    #endregion

    #region Reference File & Preview & Same File Mode

    partial void OnUseSameFileAsMasterChanged(bool value)
    {
        if (value && IsMasterCsv) { UseSameFileAsMaster = false; return; }
        ++_referenceFileLoadGeneration;
        ++_referencePreviewGeneration;
        InvalidateReferencePreview();
        ReferenceWorksheets.Clear();
        SelectedReferenceWorksheet = null;
        ReferenceHeaderRowNumber = 1;
        if (_errorSource == "Reference")
        {
            ClearError();
        }

        ResetSuccess();
        OnPropertyChanged(nameof(CanBrowseReference));

        if (value)
        {
            // 勾选同文件模式：对照表路径跟随主表并立即载入
            if (!string.IsNullOrWhiteSpace(MasterFilePath))
            {
                _ = LoadReferenceFileAsync(MasterFilePath);
            }
            else
            {
                ReferenceFilePath = null;
                ValidateOutputPathConflict();
                UpdateReadyState();
            }
        }
        else
        {
            // 取消勾选同文件模式：清空对照表路径并恢复独立选择
            ReferenceFilePath = null;
            ValidateOutputPathConflict();
            UpdateReadyState();
        }
    }

    [RelayCommand]
    public async Task BrowseReferenceFileAsync()
    {
        if (UseSameFileAsMaster)
        {
            return;
        }

        string? selectedPath = _showOpenFileDialog != null
            ? _showOpenFileDialog()
            : DefaultShowOpenFileDialog();

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            await LoadReferenceFileAsync(selectedPath);
        }
    }

    public async Task LoadReferenceFileAsync(string filePath)
    {
        int currentLoadGeneration = ++_referenceFileLoadGeneration;
        ++_referencePreviewGeneration;
        InvalidateReferencePreview();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex)
        {
            if (currentLoadGeneration != _referenceFileLoadGeneration)
            {
                return;
            }

            SetError("对照表文件路径无效", "所选对照表文件路径格式不正确。", ex.Message, "Reference");
            State = DataMatchingPageState.Error;
            return;
        }

        string extension = Path.GetExtension(fullPath);
        if (!TabularFileTypes.IsSupported(fullPath))
        {
            if (currentLoadGeneration != _referenceFileLoadGeneration)
            {
                return;
            }

            SetError("不支持的对照表文件格式", "支持 .xlsx、.xls、.csv 格式文件。", fullPath, "Reference");
            State = DataMatchingPageState.Error;
            return;
        }

        // 立即提交新对照表加载状态，清除旧对照表状态
        if (_errorSource == "Reference" || _errorSource == "Execute")
        {
            ClearError();
        }
        ResetSuccess();

        ReferenceFilePath = fullPath;
        ReferenceWorksheets.Clear();
        SelectedReferenceWorksheet = null;
        ReferenceHeaderRowNumber = 1;
        ReferenceAvailableColumns.Clear();
        ReturnFields.Clear();
        UpdateConditionReferenceSelections();

        ValidateOutputPathConflict();
        UpdateReadyState();

        var inspectionResult = await _inspectionService.InspectAsync(new WorkbookInspectionRequest(fullPath));

        if (currentLoadGeneration != _referenceFileLoadGeneration)
        {
            return;
        }

        if (!inspectionResult.Success)
        {
            MapOperationError(inspectionResult.Error, "Reference");
            State = DataMatchingPageState.Error;
            return;
        }

        foreach (var ws in inspectionResult.Worksheets)
        {
            ReferenceWorksheets.Add(ws);
        }

        // 默认选中第 1 个 Sheet
        if (IsReferenceCsv || ReferenceWorksheets.Count > 0)
        {
            _suppressReferencePreviewRefresh = true;
            SelectedReferenceWorksheet = IsReferenceCsv ? null : ReferenceWorksheets[0];
            _suppressReferencePreviewRefresh = false;

            await RefreshReferencePreviewAsync();
        }
    }

    private void InvalidateReferencePreview()
    {
        HasReferencePreviewData = false;
        ReferencePreviewDataTable = null;
        ReferencePreviewRowCount = 0;
        ReferenceAvailableColumns.Clear();
        ReturnFields.Clear();
        UpdateConditionReferenceSelections();
        if (State != DataMatchingPageState.Initial && State != DataMatchingPageState.Processing)
        {
            State = DataMatchingPageState.FileLoaded;
        }
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(SelectedReturnFieldsCount));
        OnPropertyChanged(nameof(TotalReturnFieldsCount));
    }

    partial void OnSelectedReferenceWorksheetChanged(WorksheetInfo? value)
    {
        if (_suppressReferencePreviewRefresh)
        {
            return;
        }

        ClearExecuteErrorIfPresent();
        ResetSuccess();
        InvalidateReferencePreview();
        _ = RefreshReferencePreviewAsync();
    }

    partial void OnReferenceHeaderRowNumberChanged(int value)
    {
        if (_suppressReferencePreviewRefresh)
        {
            return;
        }

        ClearExecuteErrorIfPresent();
        ResetSuccess();
        InvalidateReferencePreview();
        _ = RefreshReferencePreviewAsync();
    }

    [RelayCommand]
    public void IncrementReferenceHeaderRow()
    {
        ReferenceHeaderRowNumber++;
    }

    [RelayCommand]
    public void DecrementReferenceHeaderRow()
    {
        if (ReferenceHeaderRowNumber > 1)
        {
            ReferenceHeaderRowNumber--;
        }
    }

    private async Task RefreshReferencePreviewAsync()
    {
        int currentGeneration = ++_referencePreviewGeneration;

        if (string.IsNullOrWhiteSpace(ReferenceFilePath) || (!IsReferenceCsv && SelectedReferenceWorksheet is null))
        {
            InvalidateReferencePreview();
            return;
        }

        if (ReferenceHeaderRowNumber < 1)
        {
            SetError("表头行号无效", "对照表表头所在行必须是大于等于 1 的整数。", source: "Reference");
            State = DataMatchingPageState.Error;
            return;
        }

        IsReferencePreviewLoading = true;
        ReferencePreviewEmptyMessage = "正在读取对照表预览数据...";

        try
        {
            var request = new WorksheetPreviewRequest(
                new WorksheetSource(ReferenceFilePath, SelectedReferenceWorksheet?.Name, ReferenceHeaderRowNumber));

            var result = await _inspectionService.GetPreviewAsync(request);

            if (currentGeneration != _referencePreviewGeneration)
            {
                return;
            }

            if (!result.Success || result.Preview is null)
            {
                ReferencePreviewDataTable = null;
                HasReferencePreviewData = false;
                ReferencePreviewRowCount = 0;
                ReferencePreviewEmptyMessage = "加载对照表预览失败";
                MapOperationError(result.Error, "Reference");
                State = DataMatchingPageState.Error;
                return;
            }

            var preview = result.Preview;
            var table = new DataTable();

            ReferenceAvailableColumns.Clear();
            ReturnFields.Clear();

            for (int i = 0; i < preview.Columns.Count; i++)
            {
                var colRef = preview.Columns[i];
                string columnName = $"Col_{colRef.ColumnNumber}";
                string displayHeader = !string.IsNullOrWhiteSpace(colRef.HeaderText)
                    ? colRef.HeaderText
                    : $"第 {AvailableColumnItem.ToExcelColumnLetter(colRef.ColumnNumber)} 列";

                var col = table.Columns.Add(columnName, typeof(string));
                col.Caption = displayHeader;

                // 匹配条件和带回字段：只允许 HeaderText 非空的字段
                if (!string.IsNullOrWhiteSpace(colRef.HeaderText))
                {
                    ReferenceAvailableColumns.Add(new AvailableColumnItem(colRef));
                    ReturnFields.Add(new ReturnFieldItemViewModel(colRef, onSelectionChanged: OnReturnFieldSelectionChanged));
                }
            }

            ApplyReturnFieldsFilter();

            foreach (var row in preview.Rows)
            {
                var dataRow = table.NewRow();
                for (int i = 0; i < row.Cells.Count && i < table.Columns.Count; i++)
                {
                    dataRow[i] = row.Cells[i].DisplayValue ?? string.Empty;
                }
                table.Rows.Add(dataRow);
            }

            ReferencePreviewDataTable = table;
            ReferencePreviewRowCount = table.Rows.Count;
            HasReferencePreviewData = true;
            ReferencePreviewEmptyMessage = table.Rows.Count == 0 ? "工作表中没有数据行" : string.Empty;

            UpdateConditionReferenceSelections();
            OnPropertyChanged(nameof(SelectedReturnFieldsCount));
            OnPropertyChanged(nameof(TotalReturnFieldsCount));
            if (_errorSource == "Reference")
            {
                ClearError();
            }
            UpdateReadyState();
        }
        catch (Exception ex)
        {
            if (currentGeneration != _referencePreviewGeneration)
            {
                return;
            }

            ReferencePreviewDataTable = null;
            HasReferencePreviewData = false;
            ReferencePreviewRowCount = 0;
            ReferencePreviewEmptyMessage = "加载对照表预览时发生异常";
            SetError("对照表预览加载异常", "读取对照表预览数据时出错。", ex.Message, "Reference");
            State = DataMatchingPageState.Error;
        }
        finally
        {
            if (currentGeneration == _referencePreviewGeneration)
            {
                IsReferencePreviewLoading = false;
            }
        }
    }

    #endregion

    #region Conditions & Return Fields Handling

    [RelayCommand]
    public void AddCondition()
    {
        var row = new MatchingConditionRowViewModel(
            MasterAvailableColumns,
            ReferenceAvailableColumns,
            onDelete: RemoveCondition,
            onConditionChanged: OnConditionChanged);
        Conditions.Add(row);
        UpdateConditionRowIndicesAndCanDelete();
        ClearExecuteErrorIfPresent();
        ResetSuccess();
        UpdateReadyState();
        MarkProfileModifiedIfApplied();
        OnPropertyChanged(nameof(CanSaveProfile));
    }

    private void RemoveCondition(MatchingConditionRowViewModel row)
    {
        if (Conditions.Count > 1)
        {
            Conditions.Remove(row);
            UpdateConditionRowIndicesAndCanDelete();
            ClearExecuteErrorIfPresent();
            ResetSuccess();
            UpdateReadyState();
            MarkProfileModifiedIfApplied();
            OnPropertyChanged(nameof(CanSaveProfile));
        }
    }

    private void UpdateConditionRowIndicesAndCanDelete()
    {
        bool canDelete = Conditions.Count > 1;
        for (int i = 0; i < Conditions.Count; i++)
        {
            Conditions[i].Index = i + 1;
            Conditions[i].CanDelete = canDelete;
            Conditions[i].ShowAndSeparator = i > 0;
        }
    }

    private void OnConditionChanged()
    {
        ClearExecuteErrorIfPresent();
        ResetSuccess();
        UpdateReadyState();
        MarkProfileModifiedIfApplied();
        OnPropertyChanged(nameof(CanSaveProfile));
    }

    private void UpdateConditionMasterSelections()
    {
        foreach (var condition in Conditions)
        {
            if (condition.SelectedMasterColumn != null)
            {
                var matched = MasterAvailableColumns.FirstOrDefault(c => c.ColumnNumber == condition.SelectedMasterColumn.ColumnNumber);
                condition.SelectedMasterColumn = matched;
            }
        }
        OnPropertyChanged(nameof(CanSaveProfile));
    }

    private void UpdateConditionReferenceSelections()
    {
        foreach (var condition in Conditions)
        {
            if (condition.SelectedReferenceColumn != null)
            {
                var matched = ReferenceAvailableColumns.FirstOrDefault(c => c.ColumnNumber == condition.SelectedReferenceColumn.ColumnNumber);
                condition.SelectedReferenceColumn = matched;
            }
        }
        OnPropertyChanged(nameof(CanSaveProfile));
    }

    partial void OnSearchReturnFieldTextChanged(string? value)
    {
        ApplyReturnFieldsFilter();
    }

    private void ApplyReturnFieldsFilter()
    {
        string query = SearchReturnFieldText?.Trim() ?? string.Empty;
        foreach (var field in ReturnFields)
        {
            field.IsVisible = string.IsNullOrEmpty(query) || field.HeaderText.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
    }

    [RelayCommand]
    public void SelectAllReturnFields()
    {
        foreach (var field in ReturnFields.Where(f => f.IsVisible))
        {
            field.IsSelected = true;
        }
        OnReturnFieldSelectionChanged();
    }

    [RelayCommand]
    public void DeselectAllReturnFields()
    {
        foreach (var field in ReturnFields.Where(f => f.IsVisible))
        {
            field.IsSelected = false;
        }
        OnReturnFieldSelectionChanged();
    }

    [RelayCommand]
    public void InvertReturnFields()
    {
        foreach (var field in ReturnFields.Where(f => f.IsVisible))
        {
            field.IsSelected = !field.IsSelected;
        }
        OnReturnFieldSelectionChanged();
    }

    private void OnReturnFieldSelectionChanged()
    {
        ClearExecuteErrorIfPresent();
        ResetSuccess();
        OnPropertyChanged(nameof(SelectedReturnFieldsCount));
        OnPropertyChanged(nameof(TotalReturnFieldsCount));
        UpdateReadyState();
        MarkProfileModifiedIfApplied();
        OnPropertyChanged(nameof(CanSaveProfile));
    }

    partial void OnNormalizeComparisonKeysChanged(bool value)
    {
        ClearExecuteErrorIfPresent();
        ResetSuccess();
        UpdateReadyState();
        MarkProfileModifiedIfApplied();
    }

    partial void OnIsMasterFilterEnabledChanged(bool value)
    {
        if (!value) SelectedMasterFilterColumn = null;
        OnMasterFilterChanged();
    }
    partial void OnSelectedMasterFilterColumnChanged(AvailableColumnItem? value)
    {
        if (_suppressFilterColumnLoad)
        {
            if (!_isApplyingProfile)
            {
                OnMasterFilterChanged();
            }
            return;
        }
        MasterFilterValuesLoadTask = LoadMasterFilterValuesAsync(value);
        if (!_isApplyingProfile)
        {
            OnMasterFilterChanged();
        }
    }
    partial void OnSelectedMasterFilterValueChanged(ColumnValueOption? value)
    {
        if (!_isApplyingProfile)
        {
            OnMasterFilterChanged();
        }
    }

    private async Task LoadMasterFilterValuesAsync(AvailableColumnItem? column)
    {
        var generation = ++_masterFilterValuesGeneration;
        _masterFilterValuesCancellation?.Cancel();
        _masterFilterValuesCancellation?.Dispose();
        _masterFilterValuesCancellation = null;
        SelectedMasterFilterValue = null;
        MasterFilterValues.Clear();
        MasterFilterValuesMessage = null;
        IsMasterFilterValuesLoading = false;
        if (column is null || !MasterAvailableColumns.Contains(column) || string.IsNullOrWhiteSpace(MasterFilePath)) return;
        var cancellation = new CancellationTokenSource();
        _masterFilterValuesCancellation = cancellation;
        IsMasterFilterValuesLoading = true;
        MasterFilterValuesMessage = "正在读取主表列的全部候选值…";
        OnMasterFilterChanged();
        try
        {
            var source = new WorksheetSource(MasterFilePath, SelectedMasterWorksheet?.Name, MasterHeaderRowNumber);
            var result = await _inspectionService.GetColumnValuesAsync(new(source, column.Reference), cancellation.Token);
            if (generation != _masterFilterValuesGeneration) return;
            if (!result.Success)
            {
                MasterFilterValuesMessage = string.Join("\n", new[] { result.Error?.Message ?? "读取筛选值失败。", result.Error?.Detail }
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
                return;
            }
            foreach (var option in result.Values) MasterFilterValues.Add(option);
            MasterFilterValuesMessage = result.Values.Count == 0 ? "该列没有可选择的非空值。" : $"已读取 {result.Values.Count} 个实际值。";
        }
        catch (OperationCanceledException)
        {
            if (generation == _masterFilterValuesGeneration) MasterFilterValuesMessage = "候选值读取已取消，请重新选择主表列。";
        }
        catch (Exception ex)
        {
            if (generation == _masterFilterValuesGeneration) MasterFilterValuesMessage = $"读取筛选值失败：{ex.Message}";
        }
        finally
        {
            if (generation == _masterFilterValuesGeneration)
            {
                IsMasterFilterValuesLoading = false;
                OnMasterFilterChanged();
            }
        }
    }

    private void OnMasterFilterChanged()
    {
        ClearExecuteErrorIfPresent();
        ResetSuccess();
        UpdateReadyState();
        OnPropertyChanged(nameof(HasValidMasterFilter));
        MarkProfileModifiedIfApplied();
        OnPropertyChanged(nameof(CanSaveProfile));
    }

    partial void OnIsStatusColumnEnabledChanged(bool value)
    {
        ClearExecuteErrorIfPresent();
        ResetSuccess();
        UpdateReadyState();
        MarkProfileModifiedIfApplied();
        OnPropertyChanged(nameof(CanSaveProfile));
    }

    partial void OnStatusColumnNameChanged(string value)
    {
        ClearExecuteErrorIfPresent();
        ResetSuccess();
        UpdateReadyState();
        MarkProfileModifiedIfApplied();
        OnPropertyChanged(nameof(CanSaveProfile));
    }

    private void MarkProfileModifiedIfApplied()
    {
        if (!_isApplyingProfile && _appliedProfileName is not null)
        {
            ProfileStatusMessage = "当前设置已修改";
        }
        OnPropertyChanged(nameof(CanSaveProfile));
    }

    #endregion

    #region Output Path & Conflict & SaveAs

    partial void OnOutputFilePathChanged(string? value)
    {
        ClearExecuteErrorIfPresent();
        ValidateOutputPathConflict();
        UpdateReadyState();
    }

    [RelayCommand]
    public void ChangeOutputPath()
    {
        TryPromptSaveAs();
    }

    private bool TryPromptSaveAs()
    {
        string currentName = !string.IsNullOrWhiteSpace(OutputFilePath)
            ? Path.GetFileName(OutputFilePath)
            : "匹配结果.xlsx";

        string? initialDirectory = GetInitialDirectoryForSaveDialog();

        string? chosenPath = _showSaveFileDialogWithOptions != null
            ? _showSaveFileDialogWithOptions(currentName, initialDirectory)
            : (_showSaveFileDialog != null
                ? _showSaveFileDialog(currentName)
                : DefaultShowSaveFileDialog(currentName, initialDirectory));

        if (!string.IsNullOrWhiteSpace(chosenPath))
        {
            ResetSuccess();
            ClearExecuteErrorIfPresent();
            OutputFilePath = chosenPath;
            IsUserSpecifiedOutputPath = true;
            string? dir = Path.GetDirectoryName(chosenPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                _outputDirectoryService.SetRememberedDirectory(dir);
            }

            ValidateOutputPathConflict();
            UpdateReadyState();
            return true;
        }

        return false;
    }

    private string? GetInitialDirectoryForSaveDialog()
    {
        if (!string.IsNullOrWhiteSpace(OutputFilePath))
        {
            try
            {
                string? dir = Path.GetDirectoryName(OutputFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    return dir;
                }
            }
            catch
            {
            }
        }

        string? remembered = _outputDirectoryService.GetRememberedDirectory();
        if (!string.IsNullOrWhiteSpace(remembered) && Directory.Exists(remembered))
        {
            return remembered;
        }

        if (!string.IsNullOrWhiteSpace(MasterFilePath))
        {
            try
            {
                string? dir = Path.GetDirectoryName(MasterFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    return dir;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private void DeriveDefaultOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(MasterFilePath))
        {
            try
            {
                string? directory = Path.GetDirectoryName(MasterFilePath);
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(MasterFilePath);
                string extension = Path.GetExtension(MasterFilePath);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = ".xlsx";
                }

                string effectiveDirectory = _outputDirectoryService.ResolveOutputDirectory(directory ?? string.Empty);
                OutputFilePath = Path.Combine(effectiveDirectory, $"{fileNameWithoutExt}_匹配结果{extension}");
            }
            catch
            {
                OutputFilePath = null;
            }
        }
    }

    private void ValidateOutputPathConflict()
    {
        if (!string.IsNullOrWhiteSpace(OutputFilePath))
        {
            if (!string.IsNullOrWhiteSpace(MasterFilePath) && IsPathsEqual(OutputFilePath, MasterFilePath))
            {
                HasOutputConflictWithInput = true;
                OutputConflictMessage = "* 禁止覆盖主表或对照表，请选择其他输出路径";
                OnPropertyChanged(nameof(CanStart));
                return;
            }

            if (!string.IsNullOrWhiteSpace(ReferenceFilePath) && IsPathsEqual(OutputFilePath, ReferenceFilePath))
            {
                HasOutputConflictWithInput = true;
                OutputConflictMessage = "* 禁止覆盖主表或对照表，请选择其他输出路径";
                OnPropertyChanged(nameof(CanStart));
                return;
            }
        }

        HasOutputConflictWithInput = false;
        OutputConflictMessage = null;
        OnPropertyChanged(nameof(CanStart));
    }

    private static bool IsPathsEqual(string path1, string path2)
    {
        try
        {
            string full1 = Path.GetFullPath(path1);
            string full2 = Path.GetFullPath(path2);
            return string.Equals(full1, full2, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(path1, path2, StringComparison.OrdinalIgnoreCase);
        }
    }

    #endregion

    #region Ready State & Execution

    public void UpdateReadyState()
    {
        if (State == DataMatchingPageState.Processing || _isApplyingProfile)
        {
            return;
        }

        ValidateOutputPathConflict();

        if (HasError)
        {
            State = DataMatchingPageState.Error;
            OnPropertyChanged(nameof(CanStart));
            return;
        }

        bool hasValidConditions = Conditions.Count >= 1 &&
                                  Conditions.All(c => c.SelectedMasterColumn != null && c.SelectedReferenceColumn != null);

        bool hasValidReturnFields = ReturnFields.Any(f => f.IsSelected);

        bool hasValidStatusColumn = !IsStatusColumnEnabled || !string.IsNullOrWhiteSpace(StatusColumnName);

        if (!string.IsNullOrWhiteSpace(MasterFilePath) &&
            (IsMasterCsv || SelectedMasterWorksheet != null) &&
            MasterHeaderRowNumber >= 1 &&
            HasMasterPreviewData &&
            !string.IsNullOrWhiteSpace(ReferenceFilePath) &&
            (IsReferenceCsv || SelectedReferenceWorksheet != null) &&
            ReferenceHeaderRowNumber >= 1 &&
            HasReferencePreviewData &&
            hasValidConditions &&
            hasValidReturnFields &&
            hasValidStatusColumn &&
            HasValidMasterFilter &&
            !string.IsNullOrWhiteSpace(OutputFilePath) &&
            !HasOutputConflictWithInput &&
            !IsMasterPreviewLoading &&
            !IsReferencePreviewLoading)
        {
            State = DataMatchingPageState.Ready;
        }
        else if (!string.IsNullOrWhiteSpace(MasterFilePath) || !string.IsNullOrWhiteSpace(ReferenceFilePath))
        {
            State = DataMatchingPageState.FileLoaded;
        }
        else
        {
            State = DataMatchingPageState.Initial;
        }

        OnPropertyChanged(nameof(CanStart));
    }

    [RelayCommand]
    public async Task StartAsync()
    {
        if (!CanStart)
        {
            return;
        }

        await ExecuteMatchingAsync(overwrite: false);
    }

    private async Task ExecuteMatchingAsync(bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(MasterFilePath) ||
            (!IsMasterCsv && SelectedMasterWorksheet is null) ||
            string.IsNullOrWhiteSpace(ReferenceFilePath) ||
            (!IsReferenceCsv && SelectedReferenceWorksheet is null) ||
            string.IsNullOrWhiteSpace(OutputFilePath))
        {
            return;
        }

        IsProcessing = true;
        State = DataMatchingPageState.Processing;
        ClearError();
        ResetSuccess();

        ProgressPercent = 0;
        IsProgressIndeterminate = true;
        ProgressStageText = "正在准备匹配...";

        var progressHandler = new Progress<OperationProgress>(p =>
        {
            ProgressStageText = MapStageText(p.Stage);
            if (p.Percent.HasValue)
            {
                IsProgressIndeterminate = false;
                ProgressPercent = Math.Clamp(p.Percent.Value, 0, 100);
            }
            else
            {
                IsProgressIndeterminate = true;
            }
        });

        try
        {
            var request = new DataMatchingRequest(
                Master: new WorksheetSource(MasterFilePath, SelectedMasterWorksheet?.Name, MasterHeaderRowNumber),
                Reference: new WorksheetSource(ReferenceFilePath, SelectedReferenceWorksheet?.Name, ReferenceHeaderRowNumber),
                Conditions: Conditions.Select(c => new MatchingCondition(c.SelectedMasterColumn!.Reference, c.SelectedReferenceColumn!.Reference)).ToList(),
                ReturnFields: ReturnFields.Where(f => f.IsSelected).Select(f => f.Reference).ToList(),
                NormalizeComparisonKeys: NormalizeComparisonKeys,
                StatusColumn: new MatchingStatusColumnOptions
                {
                    Enabled = IsStatusColumnEnabled,
                    ColumnName = string.IsNullOrWhiteSpace(StatusColumnName) ? "匹配状态" : StatusColumnName.Trim()
                },
                OutputFilePath: OutputFilePath,
                OverwriteExistingOutput: overwrite
            )
            {
                MasterFilter = IsMasterFilterEnabled
                    ? new MasterRowFilter(SelectedMasterFilterColumn!.Reference, SelectedMasterFilterValue!.Value.RawValue) { SelectedValue = SelectedMasterFilterValue.Value }
                    : null
            };

            var result = await _matchingService.ExecuteAsync(request, progressHandler);

            if (result.Success && result.Summary is not null)
            {
                State = DataMatchingPageState.Success;
                HasSuccess = true;
                SuccessMessage = "数据匹配完成！";
                ResultOutputFilePath = result.OutputFilePath;
                ResultTotalMasterDataRowCount = result.Summary.TotalMasterDataRowCount;
                ResultMatchedCount = result.Summary.MatchedCount;
                ResultUnmatchedCount = result.Summary.UnmatchedCount;
                ResultDuplicateCount = result.Summary.DuplicateCount;
                ResultEmptyKeyCount = result.Summary.EmptyKeyCount;
                ResultSkippedCount = result.Summary.SkippedCount;
                ResultElapsed = result.Summary.Elapsed;

                ProgressPercent = 100;
                IsProgressIndeterminate = false;
                ProgressStageText = "匹配完成";
            }
            else
            {
                if (result.Error?.Code == OperationErrorCode.OutputAlreadyExists)
                {
                    IsProcessing = false;
                    State = DataMatchingPageState.FileLoaded;
                    UpdateReadyState();

                    var choice = _confirmExistingOutput != null
                        ? _confirmExistingOutput(OutputFilePath)
                        : DefaultConfirmExistingOutput(OutputFilePath);

                    if (choice == ExistingOutputChoice.Overwrite)
                    {
                        await ExecuteMatchingAsync(overwrite: true);
                        return;
                    }
                    else if (choice == ExistingOutputChoice.SaveAs)
                    {
                        bool saved = TryPromptSaveAs();
                        if (saved && !string.IsNullOrWhiteSpace(OutputFilePath) && !HasOutputConflictWithInput && State == DataMatchingPageState.Ready)
                        {
                            await ExecuteMatchingAsync(overwrite: false);
                        }
                        else
                        {
                            IsProcessing = false;
                            if (!HasOutputConflictWithInput && State != DataMatchingPageState.Error)
                            {
                                State = DataMatchingPageState.Ready;
                            }
                        }
                        return;
                    }
                    else
                    {
                        IsProcessing = false;
                        if (!HasOutputConflictWithInput && State != DataMatchingPageState.Error)
                        {
                            State = DataMatchingPageState.Ready;
                        }
                        return;
                    }
                }

                State = DataMatchingPageState.Error;
                MapOperationError(result.Error, "Execute");
                ProgressStageText = "处理失败";
            }
        }
        catch (Exception ex)
        {
            State = DataMatchingPageState.Error;
            SetError("匹配异常", "数据匹配过程中发生未预期的错误。", ex.Message, "Execute");
            ProgressStageText = "处理失败";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    #endregion

    #region Success Actions & Helpers

    [RelayCommand]
    public void OpenResultFile()
    {
        if (string.IsNullOrWhiteSpace(ResultOutputFilePath) || !File.Exists(ResultOutputFilePath))
        {
            SetError("打开文件失败", "结果文件不存在或路径无效。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(ResultOutputFilePath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetError("无法打开文件", "无法使用系统默认程序打开结果文件。", ex.Message);
        }
    }

    [RelayCommand]
    public void OpenResultFolder()
    {
        if (string.IsNullOrWhiteSpace(ResultOutputFilePath) || !File.Exists(ResultOutputFilePath))
        {
            SetError("打开文件夹失败", "结果文件不存在或路径无效。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ResultOutputFilePath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetError("无法定位文件", "无法在资源管理器中定位文件。", ex.Message);
        }
    }

    private void ResetSuccess()
    {
        if (_isApplyingProfile)
        {
            return;
        }

        if (HasSuccess)
        {
            HasSuccess = false;
            ResultOutputFilePath = null;
            ResultTotalMasterDataRowCount = 0;
            ResultMatchedCount = 0;
            ResultUnmatchedCount = 0;
            ResultDuplicateCount = 0;
            ResultEmptyKeyCount = 0;
            ResultSkippedCount = 0;
            ResultElapsed = TimeSpan.Zero;
            SuccessMessage = null;
            ProgressPercent = 0;
            ProgressStageText = null;

            UpdateReadyState();
        }
    }

    private void ClearExecuteErrorIfPresent()
    {
        if (_isApplyingProfile)
        {
            return;
        }

        if (_errorSource == "Execute")
        {
            ClearError();
        }
    }

    private void ClearError()
    {
        HasError = false;
        ErrorTitle = null;
        ErrorMessage = null;
        ErrorDetail = null;
        _errorSource = null;
    }

    private void SetError(string title, string message, string? detail = null, string? source = null)
    {
        HasError = true;
        ErrorTitle = title;
        ErrorMessage = message;
        ErrorDetail = detail;
        _errorSource = source;
    }

    private void MapOperationError(OperationError? error, string? source = null)
    {
        if (error is null)
        {
            SetError("操作失败", "未知错误。", source: source);
            return;
        }

        string title = error.Code switch
        {
            OperationErrorCode.FileNotFound => "文件不存在",
            OperationErrorCode.UnsupportedFileType => "不支持的文件格式",
            OperationErrorCode.FileLocked => "文件被占用",
            OperationErrorCode.WorkbookUnreadable => "工作簿无法读取",
            OperationErrorCode.WorksheetNotFound => "工作表不存在",
            OperationErrorCode.InvalidHeaderRow => "表头行号无效",
            OperationErrorCode.ColumnNotFound => "字段未找到",
            OperationErrorCode.InvalidConfiguration => "配置无效",
            OperationErrorCode.OutputConflictsWithInput => "输出路径冲突",
            OperationErrorCode.OutputAlreadyExists => "输出文件已存在",
            OperationErrorCode.OutputDirectoryNotWritable => "输出目录不可写",
            OperationErrorCode.FormulaCellNotAllowedForMatching => "存在公式单元格",
            OperationErrorCode.IncompleteOutputCleanupFailed => "不完整文件清理失败",
            _ => "处理失败"
        };

        string message = error.Code switch
        {
            OperationErrorCode.FileNotFound => "请确认文件路径是否正确。",
            OperationErrorCode.UnsupportedFileType => "支持 .xlsx、.xls、.csv 格式文件。",
            OperationErrorCode.FileLocked => "文件正被其他程序占用，请在 WPS / Excel 等程序中关闭该文件，然后点击开始数据匹配重试，或更改保存路径。",
            OperationErrorCode.WorkbookUnreadable => "工作簿损坏或无法安全读取，请检查文件完整性。",
            OperationErrorCode.WorksheetNotFound => "指定的工作表在文件中未找到，请重新选择工作表。",
            OperationErrorCode.InvalidHeaderRow => "表头所在行必须大于或等于 1，且所在行及其下方必须包含有效数据。",
            OperationErrorCode.ColumnNotFound => "所选字段在对应工作表中不存在，请刷新预览并重新选择。",
            OperationErrorCode.InvalidConfiguration => "匹配条件、返回字段或其它配置项未完整填写。",
            OperationErrorCode.OutputConflictsWithInput => "禁止覆盖主表或对照表，请选择其他输出路径。",
            OperationErrorCode.OutputAlreadyExists => "指定输出路径已存在同名文件。",
            OperationErrorCode.OutputDirectoryNotWritable => "输出目录不存在或无写入权限，请选择其他输出目录。",
            OperationErrorCode.FormulaCellNotAllowedForMatching => "筛选字段、匹配字段或返回字段中存在公式。当前版本不使用公式结果进行匹配，请改用普通值列后重试。",
            OperationErrorCode.IncompleteOutputCleanupFailed => "任务中断或失败，且未能清理生成的临时或不完整文件。",
            _ => error.Message
        };

        SetError(title, message, error.Detail, source);
    }

    private static string MapStageText(OperationStage stage) => stage switch
    {
        OperationStage.Reading => "正在读取工作簿...",
        OperationStage.Preparing => "正在准备匹配...",
        OperationStage.Processing => "正在匹配数据...",
        OperationStage.Writing => "正在写入结果...",
        OperationStage.Completed => "匹配完成",
        _ => "正在处理..."
    };

    private static ExistingOutputChoice DefaultConfirmExistingOutput(string filePath)
    {
        var dialog = new ExistingOutputDialog(filePath);
        if (System.Windows.Application.Current?.MainWindow != null)
        {
            dialog.Owner = System.Windows.Application.Current.MainWindow;
        }
        dialog.ShowDialog();
        return dialog.Choice;
    }

    private static string? DefaultShowSaveFileDialog(string defaultFileName, string? initialDirectory = null)
    {
        var dlg = new SaveFileDialog
        {
            Filter = TabularFileTypes.SaveFilter(defaultFileName),
            Title = "更改保存路径",
            FileName = defaultFileName,
            DefaultExt = Path.GetExtension(defaultFileName),
            AddExtension = true
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dlg.InitialDirectory = initialDirectory;
        }

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private static string? DefaultShowOpenFileDialog()
    {
        var dlg = new OpenFileDialog
        {
            Filter = TabularFileTypes.OpenFilter,
            Multiselect = false
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public void RefreshProfiles()
    {
        if (_profileStore is null) return;
        try
        {
            var result = _profileStore.List(ProcessingProfileKind.DataMatching);
            Profiles.Clear();
            foreach (var p in result.Profiles)
            {
                Profiles.Add(p);
            }
            OnPropertyChanged(nameof(HasProfiles));
            if (result.Errors.Count > 0)
            {
                ProfileStatusMessage = $"发现 {result.Errors.Count} 个损坏配置：{string.Join("；", result.Errors)}";
            }
            else if (Profiles.Count == 0)
            {
                SelectedProfile = null;
                if (_appliedProfileName is null)
                {
                    ProfileStatusMessage = "暂无保存配置";
                }
            }
            else if (SelectedProfile is not null)
            {
                SelectedProfile = Profiles.FirstOrDefault(p => string.Equals(p.Name, SelectedProfile.Name, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex)
        {
            ProfileStatusMessage = $"加载配置列表失败：{ex.Message}";
        }
    }

    [RelayCommand]
    public void SaveProfile()
    {
        if (!CanSaveProfile)
        {
            if (!HasMasterPreviewData || !HasReferencePreviewData)
            {
                ProfileStatusMessage = "请先加载主表与对照表预览数据后再保存配置。";
            }
            else if (Conditions.Count == 0 || Conditions.Any(c => c.SelectedMasterColumn == null || c.SelectedReferenceColumn == null))
            {
                ProfileStatusMessage = "请先配置完整的匹配条件后再保存配置。";
            }
            else if (!ReturnFields.Any(f => f.IsSelected))
            {
                ProfileStatusMessage = "请至少选择一个返回字段后再保存配置。";
            }
            else if (IsMasterFilterEnabled && !HasValidMasterFilter)
            {
                ProfileStatusMessage = "主表筛选已启用，但未选择有效的筛选列或候选值。";
            }
            else
            {
                ProfileStatusMessage = "当前匹配配置不完整，无法保存。";
            }
            return;
        }

        if (_profileStore is null)
        {
            ProfileStatusMessage = "配置存储服务未初始化。";
            return;
        }

        if (_profileValidator is null)
        {
            ProfileStatusMessage = "未保存：配置校验服务未初始化。";
            return;
        }

        string? name = _promptProfileName != null
            ? _promptProfileName("保存当前匹配设置")
            : DefaultPromptProfileName("保存当前匹配设置");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        bool exists = Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        bool overwrite = false;
        if (exists)
        {
            bool confirmed = _confirmOverwrite != null ? _confirmOverwrite(name) : DefaultConfirmOverwrite(name);
            if (!confirmed)
            {
                return;
            }
            overwrite = true;
        }

        var conditions = Conditions.Select(c => new MatchingCondition(
            c.SelectedMasterColumn!.Reference,
            c.SelectedReferenceColumn!.Reference)).ToList();

        var returnFields = ReturnFields.Where(f => f.IsSelected).Select(f => f.Reference).ToList();

        var statusSettings = new ProfileStatusSettings(IsStatusColumnEnabled, StatusColumnName ?? "匹配状态");

        ProfileFilterSettings? filterSettings = null;
        if (IsMasterFilterEnabled && SelectedMasterFilterColumn != null && SelectedMasterFilterValue != null)
        {
            filterSettings = new ProfileFilterSettings(
                SelectedMasterFilterColumn.Reference,
                SelectedMasterFilterValue.Value.Kind,
                SelectedMasterFilterValue.Value.RawValue,
                SelectedMasterFilterValue.Value.HasTime);
        }

        var matchingSettings = new MatchingProfileSettings(
            conditions,
            returnFields,
            NormalizeComparisonKeys,
            statusSettings,
            filterSettings);

        var profile = new ProcessingProfile(
            Version: 1,
            Name: name,
            Kind: ProcessingProfileKind.DataMatching,
            Format: null,
            Matching: matchingSettings);

        var errors = _profileValidator.Validate(profile);
        if (errors.Count > 0)
        {
            ProfileStatusMessage = $"配置验证失败：{string.Join("；", errors)}";
            return;
        }

        var writeResult = _profileStore.Save(profile, overwrite);
        if (writeResult.NameConflict && !overwrite)
        {
            bool confirmed = _confirmOverwrite != null ? _confirmOverwrite(name) : DefaultConfirmOverwrite(name);
            if (!confirmed)
            {
                return;
            }
            writeResult = _profileStore.Save(profile, overwrite: true);
        }

        if (!writeResult.Success)
        {
            ProfileStatusMessage = $"保存配置失败：{writeResult.Error ?? "未知错误"}";
            return;
        }

        RefreshProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        _appliedProfileName = name;
        ProfileStatusMessage = $"已保存：{name}";
    }

    [RelayCommand]
    public async Task ApplyProfileAsync()
    {
        if (!CanApplyProfile || SelectedProfile is null || _profileStore is null)
        {
            return;
        }

        if (_profileValidator is null)
        {
            ProfileStatusMessage = "未应用：配置校验服务未初始化。";
            return;
        }

        if (!HasMasterPreviewData || !HasReferencePreviewData ||
            IsMasterPreviewLoading || IsReferencePreviewLoading ||
            string.IsNullOrWhiteSpace(MasterFilePath) || string.IsNullOrWhiteSpace(ReferenceFilePath) ||
            (!IsMasterCsv && SelectedMasterWorksheet == null) ||
            (!IsReferenceCsv && SelectedReferenceWorksheet == null) ||
            MasterHeaderRowNumber < 1 || ReferenceHeaderRowNumber < 1)
        {
            ProfileStatusMessage = "未应用：请先选择主表和对照表并完成预览，再应用匹配配置。";
            return;
        }

        // Cancel in-flight filter loading and invalidate generation
        _masterFilterValuesGeneration++;
        _masterFilterValuesCancellation?.Cancel();
        _masterFilterValuesCancellation?.Dispose();
        _masterFilterValuesCancellation = null;
        IsMasterFilterValuesLoading = false;

        int currentValidationGen = ++_profileValidationGeneration;
        string? currentMasterPath = MasterFilePath;
        string? currentRefPath = ReferenceFilePath;
        string? currentMasterSheet = SelectedMasterWorksheet?.Name;
        string? currentRefSheet = SelectedReferenceWorksheet?.Name;
        int currentMasterHeader = MasterHeaderRowNumber;
        int currentRefHeader = ReferenceHeaderRowNumber;
        int currentMasterPreviewGen = _masterPreviewGeneration;
        int currentRefPreviewGen = _referencePreviewGeneration;

        IsProfileValidating = true;
        ProfileStatusMessage = "正在校验配置…";

        try
        {
            var loadResult = _profileStore.Load(ProcessingProfileKind.DataMatching, SelectedProfile.Name);
            if (!loadResult.Success || loadResult.Profile?.Matching is null)
            {
                ProfileStatusMessage = $"未应用：{loadResult.Error ?? "无法读取该配置或匹配配置为空"}";
                return;
            }

            var profile = loadResult.Profile;
            var errors = _profileValidator.Validate(profile);
            if (errors.Count > 0)
            {
                ProfileStatusMessage = $"未应用：{string.Join("；", errors)}";
                return;
            }

            var masterSource = new WorksheetSource(MasterFilePath, SelectedMasterWorksheet?.Name, MasterHeaderRowNumber);
            var masterCols = MasterAvailableColumns.Select(c => c.Reference).ToList();
            var refCols = ReferenceAvailableColumns.Select(c => c.Reference).ToList();

            var prepResult = await _profileValidator.PrepareMatchingAsync(profile, masterSource, masterCols, refCols);

            if (currentValidationGen != _profileValidationGeneration ||
                currentMasterPreviewGen != _masterPreviewGeneration ||
                currentRefPreviewGen != _referencePreviewGeneration ||
                !string.Equals(currentMasterPath, MasterFilePath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(currentRefPath, ReferenceFilePath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(currentMasterSheet, SelectedMasterWorksheet?.Name, StringComparison.Ordinal) ||
                !string.Equals(currentRefSheet, SelectedReferenceWorksheet?.Name, StringComparison.Ordinal) ||
                currentMasterHeader != MasterHeaderRowNumber ||
                currentRefHeader != ReferenceHeaderRowNumber)
            {
                if (currentValidationGen == _profileValidationGeneration)
                {
                    ProfileStatusMessage = "未应用：数据源已发生变更。";
                }
                return;
            }

            if (!prepResult.Success)
            {
                ProfileStatusMessage = $"未应用：{string.Join("；", prepResult.Errors)}";
                return;
            }

            _isApplyingProfile = true;
            _suppressFilterColumnLoad = true;
            try
            {
                NormalizeComparisonKeys = profile.Matching.NormalizeComparisonKeys;
                IsStatusColumnEnabled = profile.Matching.StatusColumn.Enabled;
                StatusColumnName = profile.Matching.StatusColumn.ColumnName;

                Conditions.Clear();
                foreach (var cond in profile.Matching.Conditions)
                {
                    var masterCol = MasterAvailableColumns.FirstOrDefault(c =>
                        c.Reference.ColumnNumber == cond.MasterColumn.ColumnNumber &&
                        c.Reference.HeaderText == cond.MasterColumn.HeaderText);
                    var refCol = ReferenceAvailableColumns.FirstOrDefault(c =>
                        c.Reference.ColumnNumber == cond.ReferenceColumn.ColumnNumber &&
                        c.Reference.HeaderText == cond.ReferenceColumn.HeaderText);

                    var row = new MatchingConditionRowViewModel(
                        MasterAvailableColumns,
                        ReferenceAvailableColumns,
                        onDelete: RemoveCondition,
                        onConditionChanged: OnConditionChanged)
                    {
                        SelectedMasterColumn = masterCol,
                        SelectedReferenceColumn = refCol
                    };
                    Conditions.Add(row);
                }
                UpdateConditionRowIndicesAndCanDelete();

                var targetRefs = profile.Matching.ReturnFields.ToHashSet();
                foreach (var field in ReturnFields)
                {
                    field.IsSelected = targetRefs.Contains(field.Reference);
                }
                OnPropertyChanged(nameof(SelectedReturnFieldsCount));
                OnPropertyChanged(nameof(TotalReturnFieldsCount));

                if (profile.Matching.MasterFilter == null)
                {
                    IsMasterFilterEnabled = false;
                    SelectedMasterFilterColumn = null;
                    MasterFilterValues.Clear();
                    SelectedMasterFilterValue = null;
                    MasterFilterValuesMessage = null;
                }
                else
                {
                    IsMasterFilterEnabled = true;
                    var filterCol = MasterAvailableColumns.FirstOrDefault(c =>
                        c.Reference.ColumnNumber == profile.Matching.MasterFilter.Column.ColumnNumber &&
                        c.Reference.HeaderText == profile.Matching.MasterFilter.Column.HeaderText);
                    SelectedMasterFilterColumn = filterCol;
                    MasterFilterValues.Clear();
                    foreach (var val in prepResult.FilterValues)
                    {
                        MasterFilterValues.Add(val);
                    }
                    SelectedMasterFilterValue = prepResult.SelectedFilterValue;
                    MasterFilterValuesMessage = prepResult.FilterValues.Count == 0
                        ? "该列没有可选择的非空值。"
                        : $"已读取 {prepResult.FilterValues.Count} 个实际值。";
                }
            }
            finally
            {
                _suppressFilterColumnLoad = false;
                _isApplyingProfile = false;
            }

            // Invalidate the previous result only after the complete profile was applied.
            ResetSuccess();
            _appliedProfileName = profile.Name;
            ProfileStatusMessage = $"已应用：{profile.Name}";
            UpdateReadyState();
            OnPropertyChanged(nameof(CanSaveProfile));
        }
        catch (OperationCanceledException)
        {
            if (currentValidationGen == _profileValidationGeneration)
            {
                ProfileStatusMessage = "配置校验已取消。";
            }
        }
        catch (Exception ex)
        {
            if (currentValidationGen == _profileValidationGeneration)
            {
                ProfileStatusMessage = $"未应用：{ex.Message}";
            }
        }
        finally
        {
            if (currentValidationGen == _profileValidationGeneration)
            {
                IsProfileValidating = false;
            }
        }
    }

    [RelayCommand]
    public void DeleteProfile()
    {
        if (!CanDeleteProfile || SelectedProfile is null || _profileStore is null)
        {
            return;
        }

        string name = SelectedProfile.Name;
        bool confirmed = _confirmDelete != null ? _confirmDelete(name) : DefaultConfirmDelete(name);
        if (!confirmed)
        {
            return;
        }

        var deleteResult = _profileStore.Delete(ProcessingProfileKind.DataMatching, name);
        if (!deleteResult.Success)
        {
            ProfileStatusMessage = $"删除配置失败：{deleteResult.Error ?? "未知错误"}";
            return;
        }

        RefreshProfiles();
        SelectedProfile = null;
        if (string.Equals(_appliedProfileName, name, StringComparison.OrdinalIgnoreCase))
        {
            ProfileStatusMessage = "当前设置已修改";
        }
    }

    private static string? DefaultPromptProfileName(string prompt)
    {
        var dialog = new SaveProfileDialog(prompt);
        if (System.Windows.Application.Current?.MainWindow is { } owner && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        return dialog.ShowDialog() == true ? dialog.ProfileName : null;
    }

    private static bool DefaultConfirmOverwrite(string profileName)
    {
        var dialog = new ConfirmProfileDialog(
            title: "确认覆盖配置",
            mainMessage: $"已存在名为“{profileName}”的配置，是否覆盖？",
            subMessage: "覆盖后将以当前页面设置替换已保存的配置。",
            confirmButtonText: "覆盖",
            isDestructive: true);
        if (System.Windows.Application.Current?.MainWindow is { } owner && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        return dialog.ShowDialog() == true;
    }

    private static bool DefaultConfirmDelete(string profileName)
    {
        var dialog = new ConfirmProfileDialog(
            title: "确认删除配置",
            mainMessage: $"确定要删除配置“{profileName}”吗？",
            subMessage: "删除后该配置将不再可用。当前页面已应用的设置和文件保持不变。",
            confirmButtonText: "删除",
            isDestructive: true);
        if (System.Windows.Application.Current?.MainWindow is { } owner && owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        return dialog.ShowDialog() == true;
    }

    #endregion
}
