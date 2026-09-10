using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TabularStudio.App.Services;
using TabularStudio.Core.Contracts;

namespace TabularStudio.App.ViewModels;

public sealed partial class ComparisonInputViewModel : ObservableObject
{
    private readonly IWorkbookInspectionService inspection;
    public event Action? Changed;
    public ObservableCollection<WorksheetInfo> Worksheets { get; } = [];
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private WorksheetInfo? _selectedWorksheet;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private string _errorMessage = "";
    public string FileName => Path.GetFileName(FilePath);
    public bool IsCsv => TabularFileTypes.IsCsv(FilePath);
    public bool CanSelectSheet => IsLoaded && !IsCsv;
    public bool IsReady => IsLoaded && (IsCsv || SelectedWorksheet is not null);

    public ComparisonInputViewModel(IWorkbookInspectionService inspection)
    {
        this.inspection = inspection;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FilePath) or nameof(SelectedWorksheet) or nameof(IsLoading) or nameof(IsLoaded))
            {
                OnPropertyChanged(nameof(IsReady)); OnPropertyChanged(nameof(IsCsv)); OnPropertyChanged(nameof(CanSelectSheet));
                OnPropertyChanged(nameof(FileName));
                Changed?.Invoke();
            }
        };
    }

    public async Task LoadAsync(string path)
    {
        if (IsLoading) return;
        IsLoading = true; IsLoaded = false; ErrorMessage = "";
        SelectedWorksheet = null; Worksheets.Clear(); FilePath = path;
        try
        {
            var result = await inspection.InspectAsync(new(path));
            if (!result.Success) { ErrorMessage = result.Error?.Message ?? "读取失败。"; return; }
            foreach (var sheet in result.Worksheets) Worksheets.Add(sheet);
            SelectedWorksheet = Worksheets.FirstOrDefault(); IsLoaded = true;
        }
        catch (Exception) { ErrorMessage = "无法读取文件，请检查文件及访问权限。"; }
        finally { IsLoading = false; }
    }

    public ComparisonSource Source() => new(FilePath, IsCsv ? null : SelectedWorksheet?.Name);
}

public sealed record ComparisonDifferenceRow(CellDifference Difference)
{
    public string Address => Difference.Address;
    public string LeftType => Difference.Left.Type;
    public string RightType => Difference.Right.Type;
    public string LeftValue => Display(Difference.Left);
    public string RightValue => Display(Difference.Right);
    private static string Display(ComparisonValue value) => value.Type == "空" ? "（空）" : value.Type == "文本"
        ? JsonSerializer.Serialize(value.Value, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
        : value.Value;
}

public sealed partial class TableComparisonViewModel : ObservableObject
{
    private readonly ITableComparisonService comparison;
    private readonly IOutputDirectoryPreferenceService preferences;
    private readonly Func<string, string?>? openFile;
    private readonly Func<string, string?>? saveFile;
    private CancellationTokenSource? cancellation;
    private int revision;
    public ComparisonInputViewModel Left { get; }
    public ComparisonInputViewModel Right { get; }
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _summary = "请选择两张表格，按相同行列位置比较数据。";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _reportPath = "";
    [ObservableProperty] private TableComparisonResult? _result;
    [ObservableProperty] private IReadOnlyList<ComparisonDifferenceRow> _differences = [];
    public bool CanConfigure => !IsBusy && !Left.IsLoading && !Right.IsLoading;
    public bool CanStart => CanConfigure && Left.IsReady && Right.IsReady;
    public bool CanExport => CanConfigure && Result is { Success: true };
    public string DifferenceHint => Result is { Success: true } r
        ? $"差异 {r.Differences.Count:N0} 处，当前显示前 {Math.Min(1000, r.Differences.Count):N0} 处；导出报告包含全部差异。文本加引号，换行和 Tab 用转义符显示。" : "";
    public string ExtentHint => Result is { Success: true } r
        ? $"表一：{r.LeftExtent!.Rows:N0} 行 × {r.LeftExtent.Columns:N0} 列    表二：{r.RightExtent!.Rows:N0} 行 × {r.RightExtent.Columns:N0} 列" : "";

    public TableComparisonViewModel(IWorkbookInspectionService inspection, ITableComparisonService comparison,
        IOutputDirectoryPreferenceService? preferences = null, Func<string, string?>? openFile = null, Func<string, string?>? saveFile = null)
    {
        this.comparison = comparison; this.preferences = preferences ?? new OutputDirectoryPreferenceService();
        this.openFile = openFile; this.saveFile = saveFile;
        Left = new(inspection); Right = new(inspection);
        Left.Changed += Invalidate; Right.Changed += Invalidate;
    }

    private void Invalidate()
    {
        revision++; cancellation?.Cancel(); Result = null; Differences = []; ReportPath = ""; ErrorMessage = "";
        Summary = "选择完成后点击“开始对比”。"; Refresh();
    }

    partial void OnIsBusyChanged(bool value) => Refresh();
    partial void OnResultChanged(TableComparisonResult? value) => Refresh();
    private void Refresh()
    {
        OnPropertyChanged(nameof(CanConfigure)); OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(DifferenceHint)); OnPropertyChanged(nameof(ExtentHint));
        StartCommand.NotifyCanExecuteChanged(); ExportCommand.NotifyCanExecuteChanged();
        BrowseLeftCommand.NotifyCanExecuteChanged(); BrowseRightCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private async Task BrowseLeftAsync() => await BrowseAsync(Left, "选择表一");
    [RelayCommand(CanExecute = nameof(CanConfigure))]
    private async Task BrowseRightAsync() => await BrowseAsync(Right, "选择表二");
    private async Task BrowseAsync(ComparisonInputViewModel input, string title)
    {
        if (!CanConfigure) return;
        string? path;
        if (openFile is not null) path = openFile(title);
        else
        {
            var dialog = new OpenFileDialog { Filter = TabularFileTypes.OpenFilter, Title = title, CheckFileExists = true };
            path = dialog.ShowDialog() == true ? dialog.FileName : null;
        }
        if (!string.IsNullOrWhiteSpace(path)) await input.LoadAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartAsync()
    {
        if (!CanStart) return;
        var currentRevision = revision;
        Result = null; Differences = []; ReportPath = ""; ErrorMessage = ""; ProgressPercent = 0;
        Summary = "正在读取并比较数据…"; IsBusy = true;
        using var current = new CancellationTokenSource(); cancellation = current;
        try
        {
            var progress = new Progress<OperationProgress>(p =>
            {
                if (cancellation == current && !current.IsCancellationRequested && currentRevision == revision)
                    ProgressPercent = p.Percent ?? 0;
            });
            var result = await comparison.CompareAsync(new(Left.Source(), Right.Source()), progress, current.Token);
            current.Token.ThrowIfCancellationRequested();
            if (currentRevision != revision) return;
            if (!result.Success)
            {
                ErrorMessage = result.Error?.Message ?? "比较失败。"; Summary = "比较未完成，无法判断是否一致。"; return;
            }
            Result = result;
            Differences = result.Differences.Take(1000).Select(d => new ComparisonDifferenceRow(d)).ToArray();
            ProgressPercent = 100;
            Summary = result.AreEqual ? "数据完全一致" : $"数据不一致：发现 {result.Differences.Count:N0} 处差异";
        }
        catch (OperationCanceledException) { Summary = "已取消比较，未生成一致性结论。"; }
        catch (Exception) { ErrorMessage = "比较失败，请检查文件后重试。"; Summary = "比较未完成，无法判断是否一致。"; }
        finally { cancellation = null; IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    public void Cancel() => cancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanExport))]
    public async Task ExportAsync()
    {
        if (!CanExport) return;
        var directory = preferences.ResolveOutputDirectory(Path.GetDirectoryName(Path.GetFullPath(Left.FilePath))!);
        var suggested = Path.Combine(directory, $"表格对比_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        string? path;
        if (saveFile is not null) path = saveFile(suggested);
        else
        {
            var dialog = new SaveFileDialog { Filter = "Excel 比较报告 (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", AddExtension = true,
                FileName = Path.GetFileName(suggested), InitialDirectory = directory, OverwritePrompt = false, Title = "导出全部差异（请使用新文件名）" };
            path = dialog.ShowDialog() == true ? dialog.FileName : null;
        }
        if (string.IsNullOrWhiteSpace(path)) return;
        await ExportToAsync(path);
    }

    public async Task ExportToAsync(string path)
    {
        if (!CanExport) return;
        var result = Result!;
        var previousSummary = Summary;
        Summary = "正在导出全部差异…";
        ErrorMessage = ""; ReportPath = ""; IsBusy = true;
        using var current = new CancellationTokenSource(); cancellation = current;
        try
        {
            var error = await comparison.ExportAsync(result, path, current.Token);
            if (error is not null) ErrorMessage = error.Message;
            else
            {
                ReportPath = Path.GetFullPath(path);
                preferences.SetRememberedDirectory(Path.GetDirectoryName(ReportPath));
            }
        }
        catch (OperationCanceledException) { ErrorMessage = "已取消导出。"; }
        catch (Exception) { ErrorMessage = "导出失败，请检查保存位置后重试。"; }
        finally
        {
            cancellation = null; IsBusy = false;
            if (ReferenceEquals(Result, result)) Summary = previousSummary;
        }
    }
}
