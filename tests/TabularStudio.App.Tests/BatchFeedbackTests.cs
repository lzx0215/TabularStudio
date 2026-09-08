using System.IO;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.App.Tests;

public sealed class BatchFeedbackTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TabularStudio-52-" + Guid.NewGuid());
    public BatchFeedbackTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private string FilePath(string name)
    { var path = Path.Combine(root, name + ".csv"); File.WriteAllText(path, $"{name},Value\r\n{name},  yes  \r\n"); return path; }
    private BatchFormatViewModel Create(Func<bool, string, string[]?>? open = null, IBatchFormatStandardizationService? batch = null)
        => new(new WorkbookInspectionService(), new FormatStandardizationService(), new OutputDirectoryPreferenceService(Path.Combine(root, "prefs.json")), batch, openFiles: open);

    [Fact]
    public async Task DialogAddsMultipleFilesAndSubsequentSelectionsAppendWithoutDuplicates()
    {
        var a = FilePath("a"); var b = FilePath("b"); var c = FilePath("c");
        var queue = new Queue<string[]?>(new[] { new[] { a, b }, new[] { b, c }, null });
        var vm = Create((multiple, _) => { Assert.True(multiple); return queue.Dequeue(); });
        await vm.BrowseFilesAsync(); var originalEditor = vm.Files[0].Editor;
        await vm.BrowseFilesAsync(); await vm.BrowseFilesAsync();
        Assert.Equal(new[] { a, b, c }, vm.Files.Select(f => f.FilePath));
        Assert.Same(originalEditor, vm.Files[0].Editor);
        Assert.Equal(c, vm.SelectedFile!.FilePath);
        await vm.LoadFilesAsync([a]); Assert.Equal(3, vm.Files.Count);
    }

    [Fact]
    public async Task CheckedReplacementResetsConfigurationAndCancelledItemStays()
    {
        var a = FilePath("a"); var b = FilePath("b"); var c = FilePath("c");
        var answers = new Queue<string[]?>([new[] { c }, null]);
        var vm = Create((multiple, _) => { Assert.False(multiple); return answers.Dequeue(); });
        await vm.LoadFilesAsync([a, b]); var retained = vm.Files[1];
        vm.Files[0].IsChecked = true; retained.IsChecked = true;
        vm.Files[0].Editor.HeaderRowNumber = 2; await vm.Files[0].Editor.RefreshPreviewAsync();
        await vm.ReplaceCheckedFilesAsync();
        Assert.Equal(c, vm.Files[0].FilePath); Assert.Equal(1, vm.Files[0].Editor.HeaderRowNumber);
        Assert.Equal("c", vm.Files[0].Editor.PreviewDataTable!.Rows[0][0]);
        Assert.Same(retained, vm.Files[1]); Assert.True(File.Exists(a));
        vm.RemoveSelected(); Assert.Single(vm.Files); Assert.Equal(c, vm.Files[0].FilePath);
        Assert.True(File.Exists(b));
    }

    [Fact]
    public async Task DuplicateReplacementKeepsOriginalAndPreviewSwitchDoesNotCheckFiles()
    {
        var a = FilePath("a"); var b = FilePath("b");
        var vm = Create((_, _) => [b]); await vm.LoadFilesAsync([a, b]);
        vm.Files[0].IsChecked = true; await vm.ReplaceCheckedFilesAsync();
        Assert.Equal(a, vm.Files[0].FilePath); Assert.Contains("重复", vm.Summary);
        vm.PreviewFile(vm.Files[1]); Assert.Equal("b", vm.SelectedFile!.Editor.PreviewDataTable!.Rows[0][0]);
        Assert.False(vm.Files[1].IsChecked);
        vm.PreviewFile(vm.Files[0]); Assert.Equal("a", vm.SelectedFile!.Editor.PreviewDataTable!.Rows[0][0]);
    }

    [Fact]
    public async Task NoRulesNeverCallsCoreAndFailureDetailsRetainSourceAndLocation()
    {
        var service = new FailingBatch(); var vm = Create(batch: service); await vm.LoadFilesAsync([FilePath("bad")]);
        Assert.False(vm.CanStart); Assert.Contains("至少选择", vm.RuleHint);
        await vm.StartAsync(); Assert.Equal(0, service.Calls);
        vm.Rules.NormalizeUnicode = true; Assert.True(vm.CanStart);
        await vm.StartAsync(); Assert.Equal(1, service.Calls);
        var failure = Assert.Single(vm.Failures);
        Assert.Equal(vm.Files[0].FilePath, failure.FilePath);
        Assert.Equal("测试失败", failure.Reason); Assert.Equal("Data!B3", failure.Detail);
        Assert.Contains("表头行：1", failure.Context); Assert.Contains("输出：", failure.Context);
        vm.Rules.NormalizeUnicode = false; Assert.False(vm.CanStart);
        await vm.StartAsync(); Assert.Equal(1, service.Calls);
    }
    private sealed class FailingBatch : IBatchFormatStandardizationService
    {
        public int Calls;
        public Task<BatchFormatResult> ExecuteAsync(BatchFormatRequest request, IProgress<BatchFormatProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new BatchFormatResult([new(0, request.Items[0], new(false, null, null, new(OperationErrorCode.ProcessingFailed, "测试失败", "Data!B3")))]));
        }
    }
}
