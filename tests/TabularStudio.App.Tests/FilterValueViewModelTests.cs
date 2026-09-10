using System.IO;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.App.Tests;

public sealed class FilterValueViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TabularStudio-filter-vm-" + Guid.NewGuid());
    public FilterValueViewModelTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private async Task<DataMatchingViewModel> Create(DelayedInspection service)
    {
        var master = Path.Combine(directory, "master.csv"); var reference = Path.Combine(directory, "ref.csv");
        File.WriteAllText(master, "Key,Group\r\na,财务\r\nb,研发\r\n");
        File.WriteAllText(reference, "Key,Value\r\na,A\r\nb,B\r\n");
        var vm = new DataMatchingViewModel(service, new DataMatchingService(),
            outputDirectoryPreferenceService: new OutputDirectoryPreferenceService(Path.Combine(directory, "preferences.json")));
        await vm.LoadMasterFileAsync(master); await vm.LoadReferenceFileAsync(reference);
        vm.Conditions[0].SelectedMasterColumn = vm.MasterAvailableColumns[0];
        vm.Conditions[0].SelectedReferenceColumn = vm.ReferenceAvailableColumns[0];
        vm.ReturnFields[1].IsSelected = true; vm.OutputFilePath = Path.Combine(directory, "result.csv");
        vm.IsMasterFilterEnabled = true;
        return vm;
    }
    private static ColumnValuesResult Values(string raw) => new(true, [new(raw, new(ColumnValueKind.Text, raw))], null);

    [Fact]
    public async Task LateResponseCannotReplaceCurrentColumnAndNoDefaultSelection()
    {
        var service = new DelayedInspection(); var vm = await Create(service);
        vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[0]; var first = vm.MasterFilterValuesLoadTask;
        Assert.True(vm.IsMasterFilterValuesLoading); Assert.False(vm.CanStart);
        vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[1]; var second = vm.MasterFilterValuesLoadTask;
        Assert.True(service.Requests[0].Token.IsCancellationRequested);
        service.Requests[1].Completion.SetResult(Values("财务")); await second;
        Assert.Null(vm.SelectedMasterFilterValue); Assert.False(vm.CanStart);
        vm.SelectedMasterFilterValue = vm.MasterFilterValues.Single(); Assert.True(vm.CanStart);
        service.Requests[0].Completion.SetResult(Values("stale")); await first;
        Assert.Equal("财务", vm.MasterFilterValues.Single().Value.RawValue);
        Assert.Equal("财务", vm.SelectedMasterFilterValue!.Value.RawValue);
        await vm.StartAsync(); Assert.True(vm.HasSuccess, vm.ErrorMessage);
        Assert.Equal(1, vm.ResultMatchedCount); Assert.Equal(1, vm.ResultSkippedCount);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("sheet")]
    [InlineData("header")]
    public async Task SourceChangeImmediatelyClearsSelectionAndInvalidatesPendingRead(string change)
    {
        var service = new DelayedInspection(); var vm = await Create(service);
        vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[1];
        var load = vm.MasterFilterValuesLoadTask;
        service.Requests[0].Completion.SetResult(Values("财务")); await load;
        vm.SelectedMasterFilterValue = vm.MasterFilterValues.Single(); Assert.True(vm.CanStart);
        vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[0]; var pending = vm.MasterFilterValuesLoadTask;
        Task? fileLoad = null;
        if (change == "file") fileLoad = vm.LoadMasterFileAsync(Path.Combine(directory, "missing.csv"));
        else if (change == "sheet") vm.SelectedMasterWorksheet = new("different");
        else vm.MasterHeaderRowNumber = 2;
        Assert.Null(vm.SelectedMasterFilterValue); Assert.Empty(vm.MasterFilterValues); Assert.False(vm.CanStart);
        Assert.True(service.Requests[1].Token.IsCancellationRequested);
        service.Requests[1].Completion.SetResult(Values("stale")); await pending;
        if (fileLoad is not null) await fileLoad;
        Assert.Empty(vm.MasterFilterValues); Assert.False(vm.CanStart);
    }

    [Fact]
    public async Task FailureClearsCandidatesBlocksExecutionAndCanRecover()
    {
        var service = new DelayedInspection(); var vm = await Create(service);
        vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[0];
        service.Requests[0].Completion.SetResult(new(false, [], new(OperationErrorCode.FileLocked, "文件被占用", "master.csv")));
        await vm.MasterFilterValuesLoadTask;
        Assert.Contains("文件被占用", vm.MasterFilterValuesMessage); Assert.Contains("master.csv", vm.MasterFilterValuesMessage);
        Assert.Empty(vm.MasterFilterValues); Assert.False(vm.CanStart);
        vm.IsMasterFilterEnabled = false; Assert.True(vm.CanStart);
        vm.IsMasterFilterEnabled = true;
        vm.SelectedMasterFilterColumn = vm.MasterAvailableColumns[1];
        service.Requests[1].Completion.SetResult(Values("研发")); await vm.MasterFilterValuesLoadTask;
        vm.SelectedMasterFilterValue = vm.MasterFilterValues.Single(); Assert.True(vm.CanStart);
    }

    private sealed class DelayedInspection : IWorkbookInspectionService
    {
        private readonly WorkbookInspectionService actual = new();
        public List<(TaskCompletionSource<ColumnValuesResult> Completion, CancellationToken Token)> Requests { get; } = [];
        public Task<WorkbookInspectionResult> InspectAsync(WorkbookInspectionRequest request, CancellationToken cancellationToken = default) => actual.InspectAsync(request, cancellationToken);
        public Task<WorksheetPreviewResult> GetPreviewAsync(WorksheetPreviewRequest request, CancellationToken cancellationToken = default) => actual.GetPreviewAsync(request, cancellationToken);
        public Task<ColumnValuesResult> GetColumnValuesAsync(ColumnValuesRequest request, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<ColumnValuesResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((completion, cancellationToken)); return completion.Task; // Intentionally ignores cancellation to test generation guard.
        }
    }
}
