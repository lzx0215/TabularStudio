using System.IO;
using ClosedXML.Excel;
using TabularStudio.App.Dialogs;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.App.Tests;

public sealed class BatchFormatViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TabularStudio-33-" + Guid.NewGuid());
    public BatchFormatViewModelTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string Input(string name, string extension)
    { var path = Path.Combine(directory, name + extension); MultiFormatViewModelTests.Write(path); return path; }
    private OutputDirectoryPreferenceService Preferences() => new(Path.Combine(directory, "preferences.json"));
    private BatchFormatViewModel Create(Func<string, ExistingOutputChoice>? confirm = null, Func<string, string?>? save = null, Action<string>? send = null)
        => new(new WorkbookInspectionService(), new FormatStandardizationService(), Preferences(), confirm: confirm, saveAs: save, sendToMatching: send);

    [Fact]
    public async Task MixedBatchUsesOneRuleSetAndPreservesFailureAndSourceFiles()
    {
        var paths = new[] { Input("a", ".xlsx"), Path.Combine(directory, "missing.csv"), Input("b", ".xls"), Input("c", ".csv") };
        var original = paths.Where(File.Exists).ToDictionary(p => p, File.ReadAllBytes);
        var vm = Create(); await vm.LoadFilesAsync(paths);
        Assert.Equal(4, vm.Files.Count); Assert.True(vm.CanStart);
        Assert.False(vm.Rules.TrimOuterWhitespace); Assert.False(vm.Rules.NormalizeUnambiguousDates);
        vm.Rules.TrimOuterWhitespace = true;
        await vm.StartAsync();
        Assert.Contains("成功 3，失败 1", vm.Summary); Assert.Equal(100, vm.ProgressPercent); Assert.True(vm.CanStart);
        Assert.StartsWith("失败", vm.Files[1].ResultText);
        foreach (var item in vm.Files.Where(f => f.ResultPath is not null))
        {
            var preview = await new WorkbookInspectionService().GetPreviewAsync(new(new(item.ResultPath!, item.Editor.SelectedWorksheet?.Name, 1)));
            Assert.Equal("yes", preview.Preview!.Rows[0].Cells[1].DisplayValue);
            Assert.Equal(original[item.FilePath], File.ReadAllBytes(item.FilePath));
        }
        Assert.False(vm.Files[3].Editor.HasWorksheetSelector);
    }

    [Fact]
    public async Task IndependentHeadersAndDirectoryMemory()
    {
        var a = Input("a", ".xlsx"); var b = Input("b", ".csv");
        using (var book = new XLWorkbook(a))
        { var sheet = book.Worksheet(1); sheet.Name = "Other"; sheet.Row(1).InsertRowsAbove(1); sheet.Cell(1, 1).Value = "preamble"; book.Save(); }
        var vm = Create(); await vm.LoadFilesAsync([a, b]);
        vm.Files[0].Editor.HeaderRowNumber = 2; await vm.Files[0].Editor.RefreshPreviewAsync();
        var output = Path.Combine(directory, "outputs"); Directory.CreateDirectory(output); vm.SetOutputDirectory(output);
        await vm.StartAsync(); Assert.All(vm.Files, f => Assert.NotNull(f.ResultPath));
        Assert.All(vm.Files, f => Assert.Equal(output, Path.GetDirectoryName(f.ResultPath)));
        var next = Create(); await next.LoadFilesAsync([b]); Assert.Equal(output, Path.GetDirectoryName(next.Files[0].Editor.OutputFilePath));
        Assert.Equal("Other", vm.Files[0].Editor.SelectedWorksheet!.Name); Assert.Equal(2, vm.Files[0].Editor.HeaderRowNumber);
    }

    [Fact]
    public async Task CancelSaveAsAndLockedOutputAllowRetryAndKeepSourceSafe()
    {
        var input = Input("source", ".csv"); var choice = ExistingOutputChoice.Cancel;
        var alternate = Path.Combine(directory, "alternate.csv");
        string? sent = null;
        var vm = Create(_ => choice, _ => alternate, p => sent = p);
        await vm.LoadFilesAsync([input]); await vm.StartAsync();
        var initial = vm.Files[0].ResultPath!; var before = File.ReadAllBytes(initial);
        await vm.StartAsync(); Assert.Contains("失败 1", vm.Summary); Assert.Equal(before, File.ReadAllBytes(initial));
        choice = ExistingOutputChoice.SaveAs; await vm.StartAsync(); Assert.True(File.Exists(alternate)); Assert.Equal(before, File.ReadAllBytes(initial));
        choice = ExistingOutputChoice.Overwrite;
        using (var locked = new FileStream(alternate, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        { await vm.StartAsync(); Assert.Contains("失败 1", vm.Summary); Assert.True(vm.CanStart); }
        await vm.StartAsync(); Assert.Contains("成功 1", vm.Summary);
        vm.SendSelectedToMatching(); Assert.Equal(alternate, sent);
        vm.Files[0].Editor.OutputFilePath = input; var sourceBytes = File.ReadAllBytes(input);
        await vm.StartAsync(); Assert.Contains("失败 1", vm.Summary); Assert.Equal(sourceBytes, File.ReadAllBytes(input));
        Assert.False(vm.CanUseSelectedResult);
    }

    [Fact]
    public async Task RunningBatchBlocksReloadAndSecondExecution()
    {
        var gate = new DelayedBatch();
        var vm = new BatchFormatViewModel(new WorkbookInspectionService(), new FormatStandardizationService(), Preferences(), gate);
        var input = Input("source", ".csv"); await vm.LoadFilesAsync([input]);
        var task = vm.StartAsync(); Assert.True(vm.IsBusy); Assert.False(vm.CanStart); Assert.False(vm.CanConfigure);
        await vm.StartAsync(); await vm.LoadFilesAsync([]); Assert.Single(vm.Files); Assert.Equal(1, gate.Calls);
        gate.Completion.SetResult(new([])); await task; Assert.False(vm.IsBusy);
    }
    private sealed class DelayedBatch : IBatchFormatStandardizationService
    {
        public int Calls;
        public TaskCompletionSource<BatchFormatResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BatchFormatResult> ExecuteAsync(BatchFormatRequest request, IProgress<BatchFormatProgress>? progress = null, CancellationToken cancellationToken = default)
        { Calls++; return Completion.Task; }
    }
}
