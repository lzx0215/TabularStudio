using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TabularStudio.App.Dialogs;
using TabularStudio.App.Models;
using TabularStudio.Core.Contracts;

namespace TabularStudio.App.ViewModels;

public sealed partial class DataMatchingViewModel : ObservableObject
{
    private readonly IWorkbookInspectionService _inspectionService;
    private readonly IDataMatchingService _matchingService;
    private readonly Func<string, ExistingOutputChoice>? _confirmExistingOutput;
    private readonly Func<string, string?>? _showSaveFileDialog;
    private readonly Func<string?>? _showOpenFileDialog;

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
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanExecuteSuccessActions))]
    private DataMatchingPageState _state = DataMatchingPageState.Initial;

    // 主表配置
    [ObservableProperty]
    private string? _masterFilePath;

    public ObservableCollection<WorksheetInfo> MasterWorksheets { get; } = [];

    [ObservableProperty]
    private WorksheetInfo? _selectedMasterWorksheet;

    [ObservableProperty]
    private int _masterHeaderRowNumber = 1;

    [ObservableProperty]
    private DataTable? _masterPreviewDataTable;

    [ObservableProperty]
    private int _masterPreviewRowCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMasterPreviewEmptyNotice))]
    private bool _hasMasterPreviewData;

    [ObservableProperty]
    private bool _isMasterPreviewLoading;

    [ObservableProperty]
    private string _masterPreviewEmptyMessage = "请先选择主表 Excel 文件以预览数据";

    public ObservableCollection<AvailableColumnItem> MasterAvailableColumns { get; } = [];

    // 对照表配置
    [ObservableProperty]
    private bool _useSameFileAsMaster;

    [ObservableProperty]
    private string? _referenceFilePath;

    public ObservableCollection<WorksheetInfo> ReferenceWorksheets { get; } = [];

    [ObservableProperty]
    private WorksheetInfo? _selectedReferenceWorksheet;

    [ObservableProperty]
    private int _referenceHeaderRowNumber = 1;

    [ObservableProperty]
    private DataTable? _referencePreviewDataTable;

    [ObservableProperty]
    private int _referencePreviewRowCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReferencePreviewEmptyNotice))]
    private bool _hasReferencePreviewData;

    [ObservableProperty]
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
    [NotifyPropertyChangedFor(nameof(CanBrowseMaster))]
    [NotifyPropertyChangedFor(nameof(CanBrowseReference))]
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

    // 派生属性
    public bool CanConfigure => !IsProcessing && State != DataMatchingPageState.Initial;

    public bool CanStart =>
        State == DataMatchingPageState.Ready &&
        !IsProcessing &&
        !string.IsNullOrWhiteSpace(MasterFilePath) &&
        SelectedMasterWorksheet != null &&
        MasterHeaderRowNumber >= 1 &&
        HasMasterPreviewData &&
        !string.IsNullOrWhiteSpace(ReferenceFilePath) &&
        SelectedReferenceWorksheet != null &&
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

    public bool CanBrowseMaster => !IsProcessing;

    public bool CanBrowseReference => !IsProcessing && !UseSameFileAsMaster;

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
        Func<string?>? showOpenFileDialog = null)
    {
        _inspectionService = inspectionService ?? throw new ArgumentNullException(nameof(inspectionService));
        _matchingService = matchingService ?? throw new ArgumentNullException(nameof(matchingService));
        _confirmExistingOutput = confirmExistingOutput;
        _showSaveFileDialog = showSaveFileDialog;
        _showOpenFileDialog = showOpenFileDialog;

        // 默认初始化 1 条匹配条件
        AddInitialCondition();
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
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            if (currentLoadGeneration != _masterFileLoadGeneration)
            {
                return;
            }

            SetError("不支持的主表文件格式", "TabularStudio 第一版仅支持 .xlsx 格式文件。", fullPath, "Master");
            State = DataMatchingPageState.Error;
            return;
        }

        // 立即提交新主表加载状态，清除旧主表状态
        if (_errorSource == "Master" || _errorSource == "Execute")
        {
            ClearError();
        }
        ResetSuccess();

        // 切换不同主表时重置用户自定义输出路径标记，以确保自动推导新主表的默认输出路径
        if (!string.Equals(MasterFilePath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            IsUserSpecifiedOutputPath = false;
        }

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
        if (MasterWorksheets.Count > 0)
        {
            _suppressMasterPreviewRefresh = true;
            SelectedMasterWorksheet = MasterWorksheets[0];
            _suppressMasterPreviewRefresh = false;

            await RefreshMasterPreviewAsync();
        }
    }

    private void InvalidateMasterPreview()
    {
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

        if (string.IsNullOrWhiteSpace(MasterFilePath) || SelectedMasterWorksheet is null)
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
                new WorksheetSource(MasterFilePath, SelectedMasterWorksheet.Name, MasterHeaderRowNumber));

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
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            if (currentLoadGeneration != _referenceFileLoadGeneration)
            {
                return;
            }

            SetError("不支持的对照表文件格式", "TabularStudio 第一版仅支持 .xlsx 格式文件。", fullPath, "Reference");
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
        if (ReferenceWorksheets.Count > 0)
        {
            _suppressReferencePreviewRefresh = true;
            SelectedReferenceWorksheet = ReferenceWorksheets[0];
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

        if (string.IsNullOrWhiteSpace(ReferenceFilePath) || SelectedReferenceWorksheet is null)
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
                new WorksheetSource(ReferenceFilePath, SelectedReferenceWorksheet.Name, ReferenceHeaderRowNumber));

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
    }

    partial void OnNormalizeComparisonKeysChanged(bool value)
    {
        ClearExecuteErrorIfPresent();
        ResetSuccess();
        UpdateReadyState();
    }

    partial void OnIsStatusColumnEnabledChanged(bool value)
    {
        ClearExecuteErrorIfPresent();
        ResetSuccess();
        UpdateReadyState();
    }

    partial void OnStatusColumnNameChanged(string value)
    {
        ClearExecuteErrorIfPresent();
        ResetSuccess();
        UpdateReadyState();
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

        string? chosenPath = _showSaveFileDialog != null
            ? _showSaveFileDialog(currentName)
            : DefaultShowSaveFileDialog(currentName);

        if (!string.IsNullOrWhiteSpace(chosenPath))
        {
            ResetSuccess();
            ClearExecuteErrorIfPresent();
            OutputFilePath = chosenPath;
            IsUserSpecifiedOutputPath = true;
            ValidateOutputPathConflict();
            UpdateReadyState();
            return true;
        }

        return false;
    }

    private void DeriveDefaultOutputPath()
    {
        if (!IsUserSpecifiedOutputPath && !string.IsNullOrWhiteSpace(MasterFilePath))
        {
            try
            {
                string? directory = Path.GetDirectoryName(MasterFilePath);
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(MasterFilePath);
                OutputFilePath = Path.Combine(directory ?? string.Empty, $"{fileNameWithoutExt}_匹配结果.xlsx");
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
        if (State == DataMatchingPageState.Processing)
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
            SelectedMasterWorksheet != null &&
            MasterHeaderRowNumber >= 1 &&
            HasMasterPreviewData &&
            !string.IsNullOrWhiteSpace(ReferenceFilePath) &&
            SelectedReferenceWorksheet != null &&
            ReferenceHeaderRowNumber >= 1 &&
            HasReferencePreviewData &&
            hasValidConditions &&
            hasValidReturnFields &&
            hasValidStatusColumn &&
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
            SelectedMasterWorksheet is null ||
            string.IsNullOrWhiteSpace(ReferenceFilePath) ||
            SelectedReferenceWorksheet is null ||
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
                Master: new WorksheetSource(MasterFilePath, SelectedMasterWorksheet.Name, MasterHeaderRowNumber),
                Reference: new WorksheetSource(ReferenceFilePath, SelectedReferenceWorksheet.Name, ReferenceHeaderRowNumber),
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
            );

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
        if (HasSuccess)
        {
            HasSuccess = false;
            ResultOutputFilePath = null;
            ResultTotalMasterDataRowCount = 0;
            ResultMatchedCount = 0;
            ResultUnmatchedCount = 0;
            ResultDuplicateCount = 0;
            ResultEmptyKeyCount = 0;
            ResultElapsed = TimeSpan.Zero;
            SuccessMessage = null;
            ProgressPercent = 0;
            ProgressStageText = null;

            UpdateReadyState();
        }
    }

    private void ClearExecuteErrorIfPresent()
    {
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
            OperationErrorCode.UnsupportedFileType => "TabularStudio 第一版仅支持 .xlsx 格式文件。",
            OperationErrorCode.FileLocked => "文件正被其他程序独占打开，请在 Excel 中关闭该文件后重试。",
            OperationErrorCode.WorkbookUnreadable => "工作簿损坏或无法安全读取，请检查文件完整性。",
            OperationErrorCode.WorksheetNotFound => "指定的工作表在文件中未找到，请重新选择工作表。",
            OperationErrorCode.InvalidHeaderRow => "表头所在行必须大于或等于 1，且所在行及其下方必须包含有效数据。",
            OperationErrorCode.ColumnNotFound => "所选字段在对应工作表中不存在，请刷新预览并重新选择。",
            OperationErrorCode.InvalidConfiguration => "匹配条件、返回字段或其它配置项未完整填写。",
            OperationErrorCode.OutputConflictsWithInput => "禁止覆盖主表或对照表，请选择其他输出路径。",
            OperationErrorCode.OutputAlreadyExists => "指定输出路径已存在同名文件。",
            OperationErrorCode.OutputDirectoryNotWritable => "输出目录不存在或无写入权限，请选择其他输出目录。",
            OperationErrorCode.FormulaCellNotAllowedForMatching => "匹配字段或返回字段中存在公式。当前版本不使用公式结果进行匹配，请改用普通值列后重试。",
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

    private static string? DefaultShowSaveFileDialog(string defaultFileName)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            FileName = defaultFileName
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private static string? DefaultShowOpenFileDialog()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            Multiselect = false
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    #endregion
}
