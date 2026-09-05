using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TabularStudio.App.Dialogs;
using TabularStudio.App.Models;
using TabularStudio.Core.Contracts;

namespace TabularStudio.App.ViewModels;

public sealed partial class FormatStandardizationViewModel : ObservableObject
{
    private readonly IWorkbookInspectionService _inspectionService;
    private readonly IFormatStandardizationService _formatService;
    private readonly Action<string>? _onSendToDataMatching;
    private readonly Func<string, ExistingOutputChoice>? _confirmExistingOutput;
    private readonly Func<string, string?>? _showSaveFileDialog;
    private readonly Func<string?>? _showOpenFileDialog;
    private bool _suppressPreviewRefresh;
    private int _previewGeneration;
    private int _fileLoadGeneration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigure))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanExecuteSuccessActions))]
    private FormatPageState _state = FormatPageState.Initial;

    [ObservableProperty]
    private string? _inputFilePath;

    public ObservableCollection<WorksheetInfo> Worksheets { get; } = [];

    [ObservableProperty]
    private WorksheetInfo? _selectedWorksheet;

    [ObservableProperty]
    private int _headerRowNumber = 1;

    // 六项可操作规则 (全部默认勾选)
    [ObservableProperty]
    private bool _trimOuterWhitespace = true;

    [ObservableProperty]
    private bool _removeTabsNewLinesAndHiddenCharacters = true;

    [ObservableProperty]
    private bool _normalizeFullWidthHalfWidth = true;

    [ObservableProperty]
    private bool _normalizeUnicode = true;

    [ObservableProperty]
    private bool _normalizeSafeNumbers = true;

    [ObservableProperty]
    private bool _normalizeUnambiguousDates = true;

    // 数据预览
    [ObservableProperty]
    private DataTable? _previewDataTable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPreviewEmptyNotice))]
    private bool _hasPreviewData;

    [ObservableProperty]
    private string _previewEmptyMessage = "请先选择 Excel 文件以预览数据";

    [ObservableProperty]
    private int _previewRowCount;

    [ObservableProperty]
    private bool _isPreviewLoading;

    // 输出路径
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private string? _outputFilePath;

    [ObservableProperty]
    private bool _isUserSpecifiedOutputPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _hasOutputConflictWithInput;

    [ObservableProperty]
    private string? _outputConflictMessage;

    // 进度与状态
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigure))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanBrowseInput))]
    private bool _isProcessing;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string? _progressStageText;

    // 成功状态
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecuteSuccessActions))]
    private string? _resultOutputFilePath;

    [ObservableProperty]
    private string? _resultWorksheetName;

    [ObservableProperty]
    private int _resultRowCount;

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

    // 派生状态属性
    public bool CanConfigure => State != FormatPageState.Processing && State != FormatPageState.Initial;

    public bool CanStart => State == FormatPageState.Ready && !IsProcessing && !HasOutputConflictWithInput && !string.IsNullOrWhiteSpace(OutputFilePath);

    public bool CanExecuteSuccessActions => State == FormatPageState.Success && !string.IsNullOrWhiteSpace(ResultOutputFilePath);

    public bool CanBrowseInput => !IsProcessing;

    public bool ShowPreviewEmptyNotice => !HasPreviewData;

    public bool HasErrorDetail => !string.IsNullOrWhiteSpace(ErrorDetail);

    public FormatStandardizationViewModel(
        IWorkbookInspectionService inspectionService,
        IFormatStandardizationService formatService,
        Action<string>? onSendToDataMatching = null,
        Func<string, ExistingOutputChoice>? confirmExistingOutput = null,
        Func<string, string?>? showSaveFileDialog = null,
        Func<string?>? showOpenFileDialog = null)
    {
        _inspectionService = inspectionService ?? throw new ArgumentNullException(nameof(inspectionService));
        _formatService = formatService ?? throw new ArgumentNullException(nameof(formatService));
        _onSendToDataMatching = onSendToDataMatching;
        _confirmExistingOutput = confirmExistingOutput;
        _showSaveFileDialog = showSaveFileDialog;
        _showOpenFileDialog = showOpenFileDialog;
    }

    partial void OnSelectedWorksheetChanged(WorksheetInfo? value)
    {
        if (_suppressPreviewRefresh)
        {
            return;
        }

        if (value is not null && State != FormatPageState.Initial && State != FormatPageState.Processing)
        {
            ResetSuccess();
            InvalidatePreviewForPendingRefresh();
            _ = RefreshPreviewAsync();
        }
    }

    partial void OnHeaderRowNumberChanged(int value)
    {
        if (State != FormatPageState.Initial && State != FormatPageState.Processing)
        {
            ResetSuccess();
            InvalidatePreviewForPendingRefresh();
            _ = RefreshPreviewAsync();
        }
    }

    partial void OnOutputFilePathChanged(string? value)
    {
        ValidateOutputPathConflict();
        UpdateReadyState();
    }

    partial void OnTrimOuterWhitespaceChanged(bool value) => ResetSuccess();
    partial void OnRemoveTabsNewLinesAndHiddenCharactersChanged(bool value) => ResetSuccess();
    partial void OnNormalizeFullWidthHalfWidthChanged(bool value) => ResetSuccess();
    partial void OnNormalizeUnicodeChanged(bool value) => ResetSuccess();
    partial void OnNormalizeSafeNumbersChanged(bool value) => ResetSuccess();
    partial void OnNormalizeUnambiguousDatesChanged(bool value) => ResetSuccess();

    [RelayCommand]
    public async Task BrowseInputFileAsync()
    {
        string? selectedPath = _showOpenFileDialog != null
            ? _showOpenFileDialog()
            : DefaultShowOpenFileDialog();

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            await LoadFileAsync(selectedPath);
        }
    }

    public async Task LoadFileAsync(string filePath)
    {
        int currentLoadGeneration = ++_fileLoadGeneration;
        ++_previewGeneration;
        InvalidatePreviewForPendingRefresh();

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
            if (currentLoadGeneration != _fileLoadGeneration)
            {
                return;
            }

            SetError("文件路径无效", "所选文件路径格式不正确。", ex.Message);
            State = FormatPageState.Error;
            return;
        }

        string extension = Path.GetExtension(fullPath);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            if (currentLoadGeneration != _fileLoadGeneration)
            {
                return;
            }

            SetError("不支持的文件格式", "TabularStudio 第一版仅支持 .xlsx 格式文件。", fullPath);
            State = FormatPageState.Error;
            return;
        }

        // 检查文件工作簿
        var inspectionResult = await _inspectionService.InspectAsync(new WorkbookInspectionRequest(fullPath));

        if (currentLoadGeneration != _fileLoadGeneration)
        {
            return;
        }

        if (!inspectionResult.Success)
        {
            MapOperationError(inspectionResult.Error);
            State = FormatPageState.Error;
            return;
        }

        ClearError();
        ResetSuccess();

        InputFilePath = fullPath;
        Worksheets.Clear();
        SelectedWorksheet = null;
        HeaderRowNumber = 1;

        // 推导默认输出路径
        if (!IsUserSpecifiedOutputPath)
        {
            try
            {
                string? directory = Path.GetDirectoryName(fullPath);
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fullPath);
                OutputFilePath = Path.Combine(directory ?? string.Empty, $"{fileNameWithoutExt}_格式统一.xlsx");
            }
            catch
            {
                OutputFilePath = null;
            }
        }

        ValidateOutputPathConflict();

        foreach (var ws in inspectionResult.Worksheets)
        {
            Worksheets.Add(ws);
        }

        State = FormatPageState.FileLoaded;

        // 默认选中第 1 个 Sheet (Approved UI Baseline 实现建议)
        if (Worksheets.Count > 0)
        {
            _suppressPreviewRefresh = true;
            SelectedWorksheet = Worksheets[0];
            _suppressPreviewRefresh = false;

            await RefreshPreviewAsync();

            if (currentLoadGeneration != _fileLoadGeneration)
            {
                return;
            }
        }
    }

    private void InvalidatePreviewForPendingRefresh()
    {
        HasPreviewData = false;
        PreviewDataTable = null;
        PreviewRowCount = 0;
        if (State != FormatPageState.Initial && State != FormatPageState.Processing)
        {
            State = FormatPageState.FileLoaded;
        }
    }

    [RelayCommand]
    public async Task RefreshPreviewAsync()
    {
        int currentGeneration = ++_previewGeneration;

        InvalidatePreviewForPendingRefresh();

        if (string.IsNullOrWhiteSpace(InputFilePath) || SelectedWorksheet is null)
        {
            return;
        }

        if (HeaderRowNumber < 1)
        {
            SetError("表头行号无效", "表头所在行必须是大于等于 1 的整数。");
            State = FormatPageState.Error;
            return;
        }

        ClearError();
        IsPreviewLoading = true;
        PreviewEmptyMessage = "正在读取前 20 行数据预览...";

        try
        {
            var request = new WorksheetPreviewRequest(
                new WorksheetSource(InputFilePath, SelectedWorksheet.Name, HeaderRowNumber));

            var previewResult = await _inspectionService.GetPreviewAsync(request);

            if (currentGeneration != _previewGeneration)
            {
                return;
            }

            if (!previewResult.Success || previewResult.Preview is null)
            {
                PreviewDataTable = null;
                HasPreviewData = false;
                PreviewRowCount = 0;
                PreviewEmptyMessage = "预览获取失败";
                MapOperationError(previewResult.Error);
                State = FormatPageState.Error;
                return;
            }

            var table = new DataTable("PreviewTable");

            // 构造列
            foreach (var col in previewResult.Preview.Columns)
            {
                string caption = col.HeaderText ?? $"第 {GetExcelColumnName(col.ColumnNumber)} 列";
                var dataCol = new DataColumn($"col_{col.ColumnNumber}", typeof(string))
                {
                    Caption = caption
                };
                table.Columns.Add(dataCol);
            }

            // 填充行数据
            foreach (var row in previewResult.Preview.Rows)
            {
                var dataRow = table.NewRow();
                for (int i = 0; i < row.Cells.Count && i < table.Columns.Count; i++)
                {
                    dataRow[i] = row.Cells[i].DisplayValue ?? string.Empty;
                }
                table.Rows.Add(dataRow);
            }

            PreviewDataTable = table;
            PreviewRowCount = table.Rows.Count;
            HasPreviewData = true;
            PreviewEmptyMessage = table.Rows.Count == 0 ? "工作表中没有数据行" : string.Empty;

            UpdateReadyState();
        }
        catch (Exception ex)
        {
            if (currentGeneration != _previewGeneration)
            {
                return;
            }

            PreviewDataTable = null;
            HasPreviewData = false;
            PreviewRowCount = 0;
            PreviewEmptyMessage = "加载预览时发生异常";
            SetError("预览加载异常", "读取表格预览数据时出错。", ex.Message);
            State = FormatPageState.Error;
        }
        finally
        {
            if (currentGeneration == _previewGeneration)
            {
                IsPreviewLoading = false;
            }
        }
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
            : "格式统一.xlsx";

        string? selectedPath = _showSaveFileDialog != null
            ? _showSaveFileDialog(currentName)
            : DefaultShowSaveFileDialog(currentName);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            OutputFilePath = selectedPath;
            IsUserSpecifiedOutputPath = true;
            ValidateOutputPathConflict();
            UpdateReadyState();
            return true;
        }

        return false;
    }

    [RelayCommand]
    public void IncrementHeaderRow()
    {
        HeaderRowNumber++;
    }

    [RelayCommand]
    public void DecrementHeaderRow()
    {
        if (HeaderRowNumber > 1)
        {
            HeaderRowNumber--;
        }
    }

    [RelayCommand]
    public async Task StartAsync()
    {
        if (!CanStart)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(InputFilePath) || SelectedWorksheet is null || string.IsNullOrWhiteSpace(OutputFilePath))
        {
            return;
        }

        // 校验输入与输出路径冲突
        if (IsPathsEqual(InputFilePath, OutputFilePath))
        {
            SetError("输出冲突", "禁止覆盖输入源文件，请选择其他输出文件名或路径。");
            return;
        }

        bool overwriteExisting = false;

        // 检查输出文件是否已存在
        if (File.Exists(OutputFilePath))
        {
            var choice = _confirmExistingOutput != null
                ? _confirmExistingOutput(OutputFilePath)
                : ShowDefaultExistingOutputDialog(OutputFilePath);

            switch (choice)
            {
                case ExistingOutputChoice.Overwrite:
                    overwriteExisting = true;
                    break;
                case ExistingOutputChoice.SaveAs:
                    bool pathChanged = TryPromptSaveAs();
                    if (!pathChanged)
                    {
                        // 用户在另存为对话框中取消，取消本次执行，保持 Ready，不调用 Core
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(OutputFilePath) || HasOutputConflictWithInput)
                    {
                        return;
                    }

                    // 另存为若依然已存在，递归或再次按确认处理
                    if (File.Exists(OutputFilePath))
                    {
                        await StartAsync();
                        return;
                    }
                    overwriteExisting = false;
                    break;
                case ExistingOutputChoice.Cancel:
                default:
                    return;
            }
        }

        // 进入 Processing 状态
        State = FormatPageState.Processing;
        IsProcessing = true;
        ClearError();
        ResetSuccess();

        ProgressPercent = 0;
        IsProgressIndeterminate = true;
        ProgressStageText = "正在准备处理...";

        var progress = new Progress<OperationProgress>(p =>
        {
            if (p.Percent.HasValue)
            {
                ProgressPercent = Math.Clamp(p.Percent.Value, 0, 100);
                IsProgressIndeterminate = false;
            }
            else
            {
                IsProgressIndeterminate = true;
            }

            ProgressStageText = MapStageToText(p.Stage, p.ProcessedRows, p.TotalRows);
        });

        var request = new FormatStandardizationRequest(
            Source: new WorksheetSource(InputFilePath, SelectedWorksheet.Name, HeaderRowNumber),
            OutputFilePath: OutputFilePath,
            Options: new FormatStandardizationOptions
            {
                TrimOuterWhitespace = TrimOuterWhitespace,
                RemoveTabsNewLinesAndHiddenCharacters = RemoveTabsNewLinesAndHiddenCharacters,
                NormalizeFullWidthHalfWidth = NormalizeFullWidthHalfWidth,
                NormalizeUnicode = NormalizeUnicode,
                NormalizeSafeNumbers = NormalizeSafeNumbers,
                NormalizeUnambiguousDates = NormalizeUnambiguousDates
            },
            OverwriteExistingOutput: overwriteExisting);

        try
        {
            var result = await _formatService.ExecuteAsync(request, progress);

            if (result.Success && result.OutputFilePath is not null && result.Summary is not null)
            {
                State = FormatPageState.Success;
                ResultOutputFilePath = result.OutputFilePath;
                ResultWorksheetName = result.Summary.ProcessedWorksheetName;
                ResultRowCount = result.Summary.ProcessedDataRowCount;
                ResultElapsed = result.Summary.Elapsed;
                SuccessMessage = $"处理完成！耗时 {ResultElapsed.TotalSeconds:F1} 秒，成功处理 {ResultRowCount:N0} 行。";
                HasSuccess = true;

                ProgressPercent = 100;
                IsProgressIndeterminate = false;
                ProgressStageText = "处理完成";
            }
            else
            {
                State = FormatPageState.Error;
                MapOperationError(result.Error);
                ProgressStageText = "处理失败";
            }
        }
        catch (Exception ex)
        {
            State = FormatPageState.Error;
            SetError("处理异常", "格式统一过程中发生未预期的错误。", ex.Message);
            ProgressStageText = "处理失败";
        }
        finally
        {
            IsProcessing = false;
        }
    }

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

    [RelayCommand]
    public void SendToDataMatching()
    {
        if (string.IsNullOrWhiteSpace(ResultOutputFilePath))
        {
            return;
        }

        _onSendToDataMatching?.Invoke(ResultOutputFilePath);
    }

    private void ValidateOutputPathConflict()
    {
        if (!string.IsNullOrWhiteSpace(InputFilePath) && !string.IsNullOrWhiteSpace(OutputFilePath))
        {
            if (IsPathsEqual(InputFilePath, OutputFilePath))
            {
                HasOutputConflictWithInput = true;
                OutputConflictMessage = "* 禁止覆盖输入源文件，请选择其他输出文件名或路径";
                return;
            }
        }

        HasOutputConflictWithInput = false;
        OutputConflictMessage = null;
    }

    private void UpdateReadyState()
    {
        if (State == FormatPageState.Processing)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(InputFilePath) &&
            SelectedWorksheet is not null &&
            HeaderRowNumber >= 1 &&
            HasPreviewData &&
            !string.IsNullOrWhiteSpace(OutputFilePath) &&
            !HasOutputConflictWithInput)
        {
            State = FormatPageState.Ready;
        }
        else if (!string.IsNullOrWhiteSpace(InputFilePath) && Worksheets.Count > 0)
        {
            State = FormatPageState.FileLoaded;
        }
    }

    private void ResetSuccess()
    {
        if (HasSuccess)
        {
            HasSuccess = false;
            ResultOutputFilePath = null;
            ResultWorksheetName = null;
            ResultRowCount = 0;
            ResultElapsed = TimeSpan.Zero;
            SuccessMessage = null;
            ProgressPercent = 0;
            ProgressStageText = null;

            UpdateReadyState();
        }
    }

    private void ClearError()
    {
        HasError = false;
        ErrorTitle = null;
        ErrorMessage = null;
        ErrorDetail = null;
    }

    private void SetError(string title, string message, string? detail = null)
    {
        HasError = true;
        ErrorTitle = title;
        ErrorMessage = message;
        ErrorDetail = detail;
    }

    private void MapOperationError(OperationError? error)
    {
        if (error is null)
        {
            SetError("未知错误", "操作未成功完成，但未提供详细错误信息。");
            return;
        }

        string title = error.Code switch
        {
            OperationErrorCode.FileNotFound => "文件未找到",
            OperationErrorCode.UnsupportedFileType => "不支持的文件格式",
            OperationErrorCode.FileLocked => "文件被占用",
            OperationErrorCode.WorkbookUnreadable => "工作簿无法读取",
            OperationErrorCode.WorksheetNotFound => "工作表不存在",
            OperationErrorCode.InvalidHeaderRow => "表头所在行无效",
            OperationErrorCode.ColumnNotFound => "指定列不存在",
            OperationErrorCode.InvalidConfiguration => "处理配置无效",
            OperationErrorCode.OutputConflictsWithInput => "输出路径冲突",
            OperationErrorCode.OutputAlreadyExists => "输出文件已存在",
            OperationErrorCode.OutputDirectoryNotWritable => "输出目录不可写",
            OperationErrorCode.FormulaCellNotAllowedForMatching => "包含公式单元格",
            OperationErrorCode.IncompleteOutputCleanupFailed => "不完整输出清理失败",
            OperationErrorCode.ProcessingFailed => "处理失败",
            _ => "操作失败"
        };

        string message = error.Code switch
        {
            OperationErrorCode.FileNotFound => "所选输入文件不存在，请重新选择有效文件。",
            OperationErrorCode.UnsupportedFileType => "TabularStudio 第一版仅支持 .xlsx 格式文件。",
            OperationErrorCode.FileLocked => "文件正被其他程序占用，请在 Excel / WPS 中关闭该文件后重试。",
            OperationErrorCode.WorkbookUnreadable => "工作簿损坏或无法安全读取，请检查文件是否完整有效。",
            OperationErrorCode.WorksheetNotFound => "指定的工作表不存在，请重新选择工作表。",
            OperationErrorCode.InvalidHeaderRow => "表头所在行号必须为大于等于 1 的整数，且不能超出工作表范围。",
            OperationErrorCode.ColumnNotFound => "请求的列在工作表中不存在，请重新刷新预览。",
            OperationErrorCode.InvalidConfiguration => "请求配置不完整或参数无效，请检查后重试。",
            OperationErrorCode.OutputConflictsWithInput => "禁止覆盖输入源文件，请选择其他输出文件名或路径。",
            OperationErrorCode.OutputAlreadyExists => "目标输出文件已存在，且未确认覆盖。",
            OperationErrorCode.OutputDirectoryNotWritable => "输出目录不存在或无写入权限，请更换保存路径。",
            OperationErrorCode.IncompleteOutputCleanupFailed => "处理失败，且清理部分输出文件失败，请手动检查目标路径。",
            OperationErrorCode.ProcessingFailed => string.IsNullOrWhiteSpace(error.Message) ? "执行格式统一处理时发生异常。" : error.Message,
            _ => error.Message
        };

        SetError(title, message, error.Detail);
    }

    private static string MapStageToText(OperationStage stage, int? processedRows, int? totalRows)
    {
        return stage switch
        {
            OperationStage.Reading => "正在读取工作簿...",
            OperationStage.Preparing => "正在准备处理...",
            OperationStage.Processing => totalRows.HasValue && totalRows.Value > 0 && processedRows.HasValue
                ? $"正在统一格式 ({processedRows.Value:N0} / {totalRows.Value:N0})..."
                : "正在统一格式...",
            OperationStage.Writing => "正在写入结果文件...",
            OperationStage.Completed => "处理完成",
            _ => "正在处理..."
        };
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
            return false;
        }
    }

    private static string GetExcelColumnName(int columnNumber)
    {
        string columnName = string.Empty;
        while (columnNumber > 0)
        {
            int modulo = (columnNumber - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            columnNumber = (columnNumber - modulo) / 26;
        }
        return columnName;
    }

    private static string? DefaultShowOpenFileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            Title = "选择 Excel 文件",
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? DefaultShowSaveFileDialog(string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            Title = "更改保存路径",
            FileName = defaultFileName
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static ExistingOutputChoice ShowDefaultExistingOutputDialog(string filePath)
    {
        var dialog = new ExistingOutputDialog(filePath)
        {
            Owner = Application.Current.MainWindow
        };

        dialog.ShowDialog();
        return dialog.Choice;
    }
}
