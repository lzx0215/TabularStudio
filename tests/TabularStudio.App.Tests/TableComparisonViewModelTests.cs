using System.IO;
using ClosedXML.Excel;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.App.Tests;

public sealed class TableComparisonViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ComparisonUI-" + Guid.NewGuid().ToString("N"));
    public TableComparisonViewModelTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string PathFor(string name) => Path.Combine(directory, name);
    private OutputDirectoryPreferenceService Preferences() => new(PathFor("preferences.json"));
    private string Csv(string name, string text) { var path = PathFor(name + ".csv"); File.WriteAllText(path, text); return path; }
    private TableComparisonViewModel Create(ITableComparisonService? service = null) =>
        new(new WorkbookInspectionService(), service ?? new TableComparisonService(), Preferences());

    [Fact]
    public async Task RealCoreFlowShowsDifferencesExportsAllAndClearsOnFileReplacement()
    {
        var vm = Create(); Assert.False(vm.CanStart); Assert.False(vm.CanExport);
        var a = Csv("a", "A,B\r\nx,1\r\n"); var b = Csv("b", "A,B\r\nx,2\r\n");
        await vm.Left.LoadAsync(a); Assert.False(vm.CanStart);
        await vm.Right.LoadAsync(b); Assert.True(vm.CanStart); Assert.False(vm.Right.CanSelectSheet);
        await vm.StartAsync(); Assert.Contains("不一致", vm.Summary); Assert.True(vm.CanExport);
        var difference = Assert.Single(vm.Differences); Assert.Equal("B2", difference.Address);
        Assert.Equal("\"1\"", difference.LeftValue);
        var output = PathFor("report.xlsx"); await vm.ExportToAsync(output);
        Assert.Equal(output, vm.ReportPath); Assert.Equal(directory, Preferences().GetRememberedDirectory());
        using (var report = new XLWorkbook(output)) Assert.Equal("B2", report.Worksheet("差异明细1").Cell(2, 1).GetString());
        await vm.Right.LoadAsync(a);
        Assert.Null(vm.Result); Assert.Empty(vm.Differences); Assert.False(vm.CanExport); Assert.Empty(vm.ReportPath);
        await vm.StartAsync(); Assert.True(vm.Result!.AreEqual); Assert.Equal("数据完全一致", vm.Summary);
    }

    [Fact]
    public async Task SheetChangeInvalidatesResultAndFailedLoadDisablesStart()
    {
        var path = PathFor("sheets.xlsx");
        using (var book = new XLWorkbook())
        { book.AddWorksheet("First").Cell("A1").Value = "a"; book.AddWorksheet("Second").Cell("A1").Value = "b"; book.SaveAs(path); }
        var vm = Create(); await vm.Left.LoadAsync(path); await vm.Right.LoadAsync(path);
        await vm.StartAsync(); Assert.True(vm.Result!.AreEqual);
        vm.Right.SelectedWorksheet = vm.Right.Worksheets[1];
        Assert.Null(vm.Result); Assert.False(vm.CanExport);
        await vm.StartAsync(); Assert.False(vm.Result!.AreEqual);
        await vm.Right.LoadAsync(PathFor("missing.xlsx"));
        Assert.False(vm.CanStart); Assert.False(vm.CanExport); Assert.NotEmpty(vm.Right.ErrorMessage);
        Assert.Null(vm.Right.SelectedWorksheet); Assert.Empty(vm.Right.Worksheets);
    }

    [Fact]
    public async Task FailedRerunCannotLeaveAnOldEqualityConclusionAndRetryWorks()
    {
        var path = Csv("a", "same\r\n"); var vm = Create();
        await vm.Left.LoadAsync(path); await vm.Right.LoadAsync(path); await vm.StartAsync();
        Assert.True(vm.Result!.AreEqual);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await vm.StartAsync(); Assert.Null(vm.Result); Assert.False(vm.CanExport);
            Assert.NotEmpty(vm.ErrorMessage); Assert.Contains("无法判断", vm.Summary);
        }
        await vm.StartAsync(); Assert.True(vm.Result!.AreEqual); Assert.Empty(vm.ErrorMessage);
    }

    [Fact]
    public async Task PreviewLimitDoesNotTruncateReport()
    {
        var a = Csv("a", string.Join("\r\n", Enumerable.Repeat("left", 1003)));
        var b = Csv("b", string.Join("\r\n", Enumerable.Repeat("right", 1003)));
        var vm = Create(); await vm.Left.LoadAsync(a); await vm.Right.LoadAsync(b); await vm.StartAsync();
        Assert.Equal(1000, vm.Differences.Count); Assert.Equal(1003, vm.Result!.Differences.Count);
        var path = PathFor("all.xlsx"); await vm.ExportToAsync(path);
        using var book = new XLWorkbook(path); Assert.Equal(1004, book.Worksheet("差异明细1").LastRowUsed()!.RowNumber());
    }

    [Fact]
    public async Task ExportFailurePreservesComparisonAndAllowsRetry()
    {
        var a = Csv("a", "x"); var vm = Create(); await vm.Left.LoadAsync(a); await vm.Right.LoadAsync(a); await vm.StartAsync();
        var path = PathFor("existing.xlsx"); File.WriteAllText(path, "keep");
        await vm.ExportToAsync(path); Assert.NotEmpty(vm.ErrorMessage); Assert.Empty(vm.ReportPath);
        Assert.True(vm.CanExport); Assert.True(vm.Result!.AreEqual); Assert.Equal("keep", File.ReadAllText(path));
        await vm.ExportToAsync(PathFor("new.xlsx")); Assert.Empty(vm.ErrorMessage); Assert.NotEmpty(vm.ReportPath);
    }

    [Fact]
    public async Task CancelDisablesActionsWhileRunningAndNeverShowsEquality()
    {
        var service = new BlockingComparison(); var vm = Create(service); var a = Csv("a", "x");
        await vm.Left.LoadAsync(a); await vm.Right.LoadAsync(a);
        var run = vm.StartAsync(); await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsBusy); Assert.False(vm.CanStart); Assert.False(vm.CanConfigure); Assert.False(vm.CanExport);
        vm.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsBusy); Assert.Null(vm.Result); Assert.True(vm.CanStart); Assert.Contains("取消", vm.Summary);
    }

    [Fact]
    public void NavigationSelectsComparisonAndPreservesExistingPages()
    {
        var vm = new MainWindowViewModel(new WorkbookInspectionService(), new FormatStandardizationService(), new DataMatchingService(), Preferences());
        vm.SelectTableComparison(); Assert.Same(vm.TableComparisonVm, vm.CurrentViewViewModel);
        Assert.True(vm.IsTableComparisonSelected); Assert.False(vm.IsDataMatchingSelected); Assert.False(vm.IsFormatStandardizationSelected);
        vm.SelectDataMatching(); Assert.Same(vm.DataMatchingVm, vm.CurrentViewViewModel); Assert.False(vm.IsTableComparisonSelected);
        vm.SelectFormatStandardization(); Assert.Same(vm.FormatStandardizationVm, vm.CurrentViewViewModel); Assert.False(vm.IsTableComparisonSelected);
    }

    private sealed class BlockingComparison : ITableComparisonService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<TableComparisonResult> CompareAsync(TableComparisonRequest request, IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Entered.SetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); throw new InvalidOperationException();
        }
        public Task<OperationError?> ExportAsync(TableComparisonResult result, string outputFilePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
