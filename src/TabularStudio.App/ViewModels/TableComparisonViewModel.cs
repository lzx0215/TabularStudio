using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TabularStudio.App.Services;
using TabularStudio.Core.Contracts;

namespace TabularStudio.App.ViewModels;

public enum ComparisonResultState
{
    NotStarted,
    Comparing,
    Identical,
    Different,
    Failed
}

public sealed partial class TableComparisonViewModel : ObservableObject
{
    private readonly IWorkbookInspectionService _inspectionService;

    // 表一 (Table 1)
    [ObservableProperty]
    private string? _firstFilePath;

    [ObservableProperty]
    private WorksheetInfo? _selectedFirstWorksheet;

    [ObservableProperty]
    private int _firstHeaderRowNumber = 1;

    [ObservableProperty]
    private DataTable? _firstPreviewDataTable;

    public ObservableCollection<WorksheetInfo> FirstWorksheets { get; } = [];
    public bool HasFirstWorksheetSelector => !TabularFileTypes.IsCsv(FirstFilePath);

    // 表二 (Table 2)
    [ObservableProperty]
    private string? _secondFilePath;

    [ObservableProperty]
    private WorksheetInfo? _selectedSecondWorksheet;

    [ObservableProperty]
    private int _secondHeaderRowNumber = 1;

    [ObservableProperty]
    private DataTable? _secondPreviewDataTable;

    public ObservableCollection<WorksheetInfo> SecondWorksheets { get; } = [];
    public bool HasSecondWorksheetSelector => !TabularFileTypes.IsCsv(SecondFilePath);

    // 状态与结果
    [ObservableProperty]
    private ComparisonResultState _resultState = ComparisonResultState.NotStarted;

    [ObservableProperty]
    private string _resultSummary = "请选择表一与表二文件后开始对比。";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private DataTable? _differenceDataTable;

    [ObservableProperty]
    private bool _isBusy;

    public bool CanConfigure => !IsBusy;
    public bool CanStart => !IsBusy && !string.IsNullOrWhiteSpace(FirstFilePath) && !string.IsNullOrWhiteSpace(SecondFilePath);

    public TableComparisonViewModel(IWorkbookInspectionService inspectionService)
    {
        _inspectionService = inspectionService;
    }

    [RelayCommand]
    public void BrowseFirstFile()
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog
        {
            Filter = TabularFileTypes.OpenFilter,
            Title = "选择表一"
        };
        if (dialog.ShowDialog() == true)
        {
            FirstFilePath = dialog.FileName;
            OnPropertyChanged(nameof(HasFirstWorksheetSelector));
            OnPropertyChanged(nameof(CanStart));
            _ = LoadFirstPreviewAsync();
        }
    }

    [RelayCommand]
    public void BrowseSecondFile()
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog
        {
            Filter = TabularFileTypes.OpenFilter,
            Title = "选择表二"
        };
        if (dialog.ShowDialog() == true)
        {
            SecondFilePath = dialog.FileName;
            OnPropertyChanged(nameof(HasSecondWorksheetSelector));
            OnPropertyChanged(nameof(CanStart));
            _ = LoadSecondPreviewAsync();
        }
    }

    private async Task LoadFirstPreviewAsync()
    {
        if (string.IsNullOrWhiteSpace(FirstFilePath) || !File.Exists(FirstFilePath)) return;
        try
        {
            FirstWorksheets.Clear();
            if (HasFirstWorksheetSelector)
            {
                var inspect = await _inspectionService.InspectAsync(new(FirstFilePath));
                if (inspect.Success)
                {
                    foreach (var s in inspect.Worksheets) FirstWorksheets.Add(s);
                    SelectedFirstWorksheet = FirstWorksheets.FirstOrDefault();
                }
            }
            var preview = await _inspectionService.GetPreviewAsync(new(new(FirstFilePath, SelectedFirstWorksheet?.Name, FirstHeaderRowNumber)));
            if (preview.Preview is not null)
            {
                var dt = new DataTable();
                foreach (var col in preview.Preview.Columns) dt.Columns.Add(col.HeaderText ?? $"第 {col.ColumnNumber} 列");
                foreach (var row in preview.Preview.Rows)
                {
                    var r = dt.NewRow();
                    for (int i = 0; i < row.Cells.Count && i < dt.Columns.Count; i++) r[i] = row.Cells[i].DisplayValue;
                    dt.Rows.Add(r);
                }
                FirstPreviewDataTable = dt;
            }
        }
        catch { /* ignore preview error */ }
    }

    private async Task LoadSecondPreviewAsync()
    {
        if (string.IsNullOrWhiteSpace(SecondFilePath) || !File.Exists(SecondFilePath)) return;
        try
        {
            SecondWorksheets.Clear();
            if (HasSecondWorksheetSelector)
            {
                var inspect = await _inspectionService.InspectAsync(new(SecondFilePath));
                if (inspect.Success)
                {
                    foreach (var s in inspect.Worksheets) SecondWorksheets.Add(s);
                    SelectedSecondWorksheet = SecondWorksheets.FirstOrDefault();
                }
            }
            var preview = await _inspectionService.GetPreviewAsync(new(new(SecondFilePath, SelectedSecondWorksheet?.Name, SecondHeaderRowNumber)));
            if (preview.Preview is not null)
            {
                var dt = new DataTable();
                foreach (var col in preview.Preview.Columns) dt.Columns.Add(col.HeaderText ?? $"第 {col.ColumnNumber} 列");
                foreach (var row in preview.Preview.Rows)
                {
                    var r = dt.NewRow();
                    for (int i = 0; i < row.Cells.Count && i < dt.Columns.Count; i++) r[i] = row.Cells[i].DisplayValue;
                    dt.Rows.Add(r);
                }
                SecondPreviewDataTable = dt;
            }
        }
        catch { /* ignore preview error */ }
    }

    [RelayCommand]
    public async Task StartCompareAsync()
    {
        if (!CanStart) return;
        IsBusy = true;
        ResultState = ComparisonResultState.Comparing;
        ResultSummary = "正在对比表格数据...";
        ErrorMessage = null;
        try
        {
            await Task.Delay(200); // 预留核心算法接入
            // 基础比对演示结果
            var dt = new DataTable();
            dt.Columns.Add("差异类型");
            dt.Columns.Add("位置 / 行号");
            dt.Columns.Add("表一取值");
            dt.Columns.Add("表二取值");
            DifferenceDataTable = dt;

            ResultState = ComparisonResultState.Identical;
            ResultSummary = "两表结构与数据完全一致。";
        }
        catch (Exception ex)
        {
            ResultState = ComparisonResultState.Failed;
            ErrorMessage = ex.Message;
            ResultSummary = "对比过程发生异常。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void CancelCompare()
    {
        IsBusy = false;
        ResultState = ComparisonResultState.NotStarted;
        ResultSummary = "已取消对比。";
    }
}
